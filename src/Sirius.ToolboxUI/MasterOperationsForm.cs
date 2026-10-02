using System.Drawing;
using System.Windows.Forms;
using Sirius.MasterData;
using Sirius.Toolbox.IO;
using Sirius.Toolbox.Master.Operations;
using Sirius.Toolbox.Master.Creation;

namespace Sirius.ToolboxUI;

public sealed class MasterOperationsForm : Form
{
    private readonly MasterOperationsService _service = new();
    private readonly EventOperationsAdapter _eventAdapter;
    private readonly GachaOperationsAdapter _gachaAdapter;

    private readonly ToolStripButton _openButton = new("打开");
    private readonly ToolStripButton _saveButton = new("保存");
    private readonly ToolStripButton _saveAsButton = new("另存为");
    private readonly ToolStripButton _verifyButton = new("校验");
    private readonly ToolStripButton _refreshButton = new("刷新全部");
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _fileLabel = new() { Text = "未打开数据库" };
    private readonly ToolStripProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 110 };

    private readonly TimedMasterOperationsPage _eventsPage;
    private readonly ExchangeShopOperationsPage _shopsPage;
    private readonly TimedMasterOperationsPage _gachasPage;
    private readonly MusicOperationsPage _musicPage;
    private readonly MasterCreationPage _creationPage;

    private string? _sourcePath;
    private string? _workingPath;
    private string? _workingDirectory;
    private bool _dirty;
    private bool _busy;

    public MasterOperationsForm(string? startupPath = null)
    {
        _eventAdapter = new EventOperationsAdapter(_service);
        _gachaAdapter = new GachaOperationsAdapter(_service);

        _eventsPage = new TimedMasterOperationsPage(
            "活动",
            "EventMaster",
            _service,
            _eventAdapter.List,
            _eventAdapter.Extend);
        _shopsPage = new ExchangeShopOperationsPage(_service);
        _gachasPage = new TimedMasterOperationsPage(
            "卡池",
            "GachaMaster",
            _service,
            _gachaAdapter.List,
            _gachaAdapter.Extend);
        _musicPage = new MusicOperationsPage(_service);
        _creationPage = new MasterCreationPage(() => _workingPath, ApplyCreationAsync);

        _eventsPage.ApplyPlanAsync = ApplyPlanAsync;
        _shopsPage.ApplyPlanAsync = ApplyPlanAsync;
        _gachasPage.ApplyPlanAsync = ApplyPlanAsync;
        _musicPage.ApplyPlanAsync = ApplyPlanAsync;

        Text = "Sirius MasterData 运营编辑器";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1540, 940);
        AllowDrop = true;

        BuildUi();
        WireEvents();
        UpdateUiState();

        var initialPath = startupPath;
        if (!string.IsNullOrWhiteSpace(initialPath))
            Shown += async (_, _) =>
            {
                if (File.Exists(initialPath)) await OpenDatabaseAsync(initialPath!);
            };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty)
        {
            var answer = MessageBox.Show(
                this,
                "运营编辑器中有未保存的更改。关闭后这些更改会丢失，确定关闭吗？",
                "未保存的更改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        CleanupWorkingCopy();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };
        var fileMenu = new ToolStripMenuItem("文件");
        fileMenu.DropDownItems.Add("打开...", null, async (_, _) => await PromptOpenAsync());
        fileMenu.DropDownItems.Add("保存", null, async (_, _) => await SaveAsync());
        fileMenu.DropDownItems.Add("另存为...", null, async (_, _) => await SaveAsAsync());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("退出", null, (_, _) => Close());
        var databaseMenu = new ToolStripMenuItem("数据库");
        databaseMenu.DropDownItems.Add("校验", null, async (_, _) => await VerifyAsync());
        databaseMenu.DropDownItems.Add("刷新全部", null, async (_, _) => await RefreshAllAsync());
        menu.Items.Add(fileMenu);
        menu.Items.Add(databaseMenu);
        MainMenuStrip = menu;

        var tools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        tools.Items.AddRange([
            _openButton,
            _saveButton,
            _saveAsButton,
            new ToolStripSeparator(),
            _verifyButton,
            _refreshButton
        ]);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreatePage("活动", _eventsPage));
        tabs.TabPages.Add(CreatePage("交换商店", _shopsPage));
        tabs.TabPages.Add(CreatePage("卡池", _gachasPage));
        tabs.TabPages.Add(CreatePage("乐曲", _musicPage));
        tabs.TabPages.Add(CreatePage("自制内容", _creationPage));

        var status = new StatusStrip { Dock = DockStyle.Bottom };
        status.Items.Add(_statusLabel);
        status.Items.Add(_progress);
        status.Items.Add(_fileLabel);

        Controls.Add(tabs);
        Controls.Add(status);
        Controls.Add(tools);
        Controls.Add(menu);
    }

    private static TabPage CreatePage(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private void WireEvents()
    {
        _openButton.Click += async (_, _) => await PromptOpenAsync();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _saveAsButton.Click += async (_, _) => await SaveAsAsync();
        _verifyButton.Click += async (_, _) => await VerifyAsync();
        _refreshButton.Click += async (_, _) => await RefreshAllAsync();

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += async (_, e) =>
        {
            var files = e.Data?.GetData(DataFormats.FileDrop) as string[];
            if (files is { Length: > 0 }) await OpenDatabaseAsync(files[0]);
        };
    }

    private async Task PromptOpenAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "MasterMemory 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            Title = "打开 MasterMemory 数据库"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await OpenDatabaseAsync(dialog.FileName);
    }

    private async Task OpenDatabaseAsync(string path)
    {
        if (_dirty)
        {
            var answer = MessageBox.Show(
                this,
                "当前文件有未保存修改。放弃修改并打开其他数据库？",
                "未保存的更改",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        await RunBusyAsync("正在打开 MasterMemory...", async () =>
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.Combine(Path.GetTempPath(), "Sirius.MasterOperations", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var working = Path.Combine(directory, "mastermemory.db");
            try
            {
                File.Copy(fullPath, working, overwrite: true);
                await Task.Run(() => MasterMemoryDatabaseService.Verify(working));
            }
            catch
            {
                try { Directory.Delete(directory, recursive: true); } catch { }
                throw;
            }

            CleanupWorkingCopy();
            _sourcePath = fullPath;
            _workingDirectory = directory;
            _workingPath = working;
            _dirty = false;
            SetPageDatabase(working);
            await RefreshAllPagesCoreAsync();
            SetStatus($"已打开 {Path.GetFileName(fullPath)}");
        });
    }

    private void SetPageDatabase(string? path)
    {
        _eventsPage.DatabasePath = path;
        _shopsPage.DatabasePath = path;
        _gachasPage.DatabasePath = path;
        _musicPage.DatabasePath = path;
    }

    private async Task RefreshAllAsync()
    {
        if (_workingPath is null) return;
        await RunBusyAsync("正在刷新运营视图...", RefreshAllPagesCoreAsync);
    }

    private async Task RefreshAllPagesCoreAsync()
    {
        await _eventsPage.RefreshAsync();
        await _shopsPage.RefreshAsync();
        await _gachasPage.RefreshAsync();
        await _musicPage.RefreshAsync();
    }

    private async Task ApplyPlanAsync(MasterOperationPlan plan)
    {
        var path = _workingPath;
        if (path is null || plan.IsEmpty)
        {
            if (plan.IsEmpty) SetStatus("没有需要应用的变更。");
            return;
        }

        var answer = MessageBox.Show(
            this,
            plan.ToPreviewText(),
            "确认 MasterData 变更",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        await RunBusyAsync("正在应用并校验 MasterData 变更...", async () =>
        {
            var result = await Task.Run(() => _service.Apply(path, plan));
            _dirty = true;
            await RefreshAllPagesCoreAsync();
            SetStatus($"已应用 {result.AppliedChanges} 条记录变更，SHA-256 {result.Sha256[..12]}...");
        });
    }

    private async Task ApplyCreationAsync(MasterCreationDraft draft)
    {
        var path = _workingPath;
        if (path is null) return;
        await RunBusyAsync("正在创建并校验自制 Master...", async () =>
        {
            var output = path + ".created";
            await Task.Run(() => new MasterCreationService().Apply(path, draft, output));
            File.Move(output, path, overwrite: true);
            _dirty = true;
            await RefreshAllPagesCoreAsync();
            SetStatus($"已创建 {draft.Kind}，共 {draft.Records.Count} 条记录。");
        });
    }

    private async Task VerifyAsync()
    {
        var path = _workingPath;
        if (path is null) return;
        await RunBusyAsync("正在校验 MasterMemory...", async () =>
        {
            var result = await Task.Run(() => MasterMemoryDatabaseService.Verify(path));
            SetStatus($"校验通过：{result.TableCount} 表 / {result.RowCount:N0} 行 / {result.Sha256[..12]}...");
            MessageBox.Show(
                this,
                $"数据库有效。\r\n\r\n表：{result.TableCount}\r\n行：{result.RowCount:N0}\r\nSHA-256：{result.Sha256}",
                "校验通过",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
    }

    private async Task SaveAsync()
    {
        if (_sourcePath is null) return;
        await SaveToAsync(_sourcePath, updateSource: false);
    }

    private async Task SaveAsAsync()
    {
        if (_workingPath is null) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "MasterMemory 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            FileName = _sourcePath is null ? "mastermemory.db" : Path.GetFileName(_sourcePath),
            Title = "另存为 MasterMemory 数据库"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await SaveToAsync(dialog.FileName, updateSource: true);
    }

    private async Task SaveToAsync(string targetPath, bool updateSource)
    {
        var working = _workingPath;
        if (working is null) return;
        await RunBusyAsync("正在保存...", async () =>
        {
            await Task.Run(() => MasterMemoryDatabaseService.Verify(working));
            var fullTarget = Path.GetFullPath(targetPath);
            if (File.Exists(fullTarget)) File.Copy(fullTarget, fullTarget + ".bak", overwrite: true);
            var bytes = await File.ReadAllBytesAsync(working);
            AtomicFile.WriteAllBytes(fullTarget, bytes);
            if (updateSource) _sourcePath = fullTarget;
            _dirty = false;
            SetStatus($"已保存 {fullTarget}");
        });
    }

    private async Task RunBusyAsync(string message, Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        SetStatus(message);
        UpdateUiState();
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            SetStatus("操作失败。");
            MessageBox.Show(this, ex.ToString(), "MasterData 运营编辑器", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateUiState();
        }
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private void UpdateUiState()
    {
        var hasDatabase = _workingPath is not null;
        _openButton.Enabled = !_busy;
        _saveButton.Enabled = !_busy && hasDatabase && _dirty;
        _saveAsButton.Enabled = !_busy && hasDatabase;
        _verifyButton.Enabled = !_busy && hasDatabase;
        _refreshButton.Enabled = !_busy && hasDatabase;
        _progress.Visible = _busy;
        _fileLabel.Text = _sourcePath is null
            ? "未打开数据库"
            : (_dirty ? "* " : string.Empty) + Path.GetFileName(_sourcePath);
        Text = _sourcePath is null
            ? "Sirius MasterData 运营编辑器"
            : $"Sirius MasterData 运营编辑器 - {(_dirty ? "*" : string.Empty)}{Path.GetFileName(_sourcePath)}";
    }

    private void CleanupWorkingCopy()
    {
        _workingPath = null;
        SetPageDatabase(null);
        if (_workingDirectory is null) return;
        try
        {
            if (Directory.Exists(_workingDirectory)) Directory.Delete(_workingDirectory, recursive: true);
        }
        catch
        {
            // Temp cleanup can be retried by the OS later.
        }
        finally
        {
            _workingDirectory = null;
        }
    }
}
