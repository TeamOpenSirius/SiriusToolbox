using System.Drawing;
using System.Windows.Forms;
using Sirius.MasterData;

namespace Sirius.ToolboxUI;

/// <summary>
/// 离线重打包窗口：导出类型化 JSON、编辑后再重建 mastermemory.db。
/// 与 baseline 一致或语义未变的表会保留原始块，因此未改动的表逐字节一致。
/// Offline repack window: export typed JSON, edit it, and rebuild mastermemory.db.
/// Tables that match the baseline (or decode to the same values) keep their original
/// payload block, so untouched tables stay byte-for-byte identical.
/// </summary>
public sealed class MasterMemoryRepackForm : Form
{
    private readonly TextBox _sourceBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _jsonBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _baselineBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _exactBox = new()
    {
        AutoSize = true,
        Checked = true,
        Text = "严格字节校验（无改动时必须与源数据库逐字节一致）"
    };
    private readonly Button _exportButton = new() { AutoSize = true, Text = "1. 导出 JSON" };
    private readonly Button _packButton = new() { AutoSize = true, Text = "2. 重打包数据库" };
    private readonly Button _cancelButton = new() { AutoSize = true, Enabled = false, Text = "取消" };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F) };
    private CancellationTokenSource? _cancellation;

    public MasterMemoryRepackForm()
    {
        Text = "主数据离线重打包";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 560);
        Size = new Size(1000, 680);
        BuildUi();
        _exportButton.Click += async (_, _) => await ExportAsync();
        _packButton.Click += async (_, _) => await PackAsync();
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();
    }

    /// <summary>预填当前编辑器的数据库，便于直接从主数据编辑器进入重打包。</summary>
    public void PresetSource(string databasePath)
    {
        if (!File.Exists(databasePath)) return;
        if (string.IsNullOrWhiteSpace(_sourceBox.Text)) _sourceBox.Text = databasePath;
        if (string.IsNullOrWhiteSpace(_outputBox.Text))
            _outputBox.Text = Path.Combine(
                Path.GetDirectoryName(databasePath) ?? ".",
                Path.GetFileNameWithoutExtension(databasePath) + "_repacked.db");
    }

    private void BuildUi()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, 0, "源数据库：", _sourceBox, "选择…", BrowseSource);
        AddRow(grid, 1, "工作 JSON 目录：", _jsonBox, "选择…", () => BrowseFolder(_jsonBox, "选择工作 JSON 目录"));
        AddRow(grid, 2, "baseline JSON 目录：", _baselineBox, "选择…", () => BrowseFolder(_baselineBox, "选择未修改的 baseline JSON 目录"));
        AddRow(grid, 3, "输出数据库：", _outputBox, "选择…", BrowseOutput);
        grid.Controls.Add(_exactBox, 1, 4);
        grid.SetColumnSpan(_exactBox, 2);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 8) };
        actions.Controls.Add(_exportButton);
        actions.Controls.Add(_packButton);
        actions.Controls.Add(_cancelButton);

        Controls.Add(_log);
        Controls.Add(grid);
        Controls.Add(actions);
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control, string buttonText, Action onClick)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = label }, 0, row);
        panel.Controls.Add(control, 1, row);
        var button = new Button { AutoSize = true, Text = buttonText };
        button.Click += (_, _) => onClick();
        panel.Controls.Add(button, 2, row);
    }

    private void BrowseSource()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "MasterMemory 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            Title = "选择源 mastermemory.db"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _sourceBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(_outputBox.Text))
                _outputBox.Text = Path.Combine(
                    Path.GetDirectoryName(dialog.FileName) ?? ".",
                    Path.GetFileNameWithoutExtension(dialog.FileName) + "_repacked.db");
        }
    }

    private void BrowseOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "MasterMemory 数据库 (*.db)|*.db|所有文件 (*.*)|*.*",
            FileName = "mastermemory.db",
            Title = "选择输出数据库"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputBox.Text = dialog.FileName;
    }

    private void BrowseFolder(TextBox target, string description)
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = description };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private async Task ExportAsync()
    {
        var source = _sourceBox.Text.Trim();
        var json = _jsonBox.Text.Trim();
        if (!File.Exists(source)) { Warn("请先选择有效的源 mastermemory.db。"); return; }
        if (string.IsNullOrWhiteSpace(json)) { Warn("请先选择工作 JSON 目录。"); return; }
        if (File.Exists(json)) { Warn("工作 JSON 目录指向了一个文件，请选择目录。"); return; }

        IProgress<string> progress = new Progress<string>(Append);
        await RunAsync("导出", async token =>
        {
            await MasterMemoryDatabaseService.ExportAllJsonAsync(source, json, progress.Report, token);
            Append($"已导出类型化 JSON：{json}");
            if (string.IsNullOrWhiteSpace(_baselineBox.Text))
                Append("建议把本次导出复制一份作为 baseline，编辑工作 JSON 后再执行重打包。");
        });
    }

    private async Task PackAsync()
    {
        var source = _sourceBox.Text.Trim();
        var json = _jsonBox.Text.Trim();
        var baseline = _baselineBox.Text.Trim();
        var output = _outputBox.Text.Trim();
        if (!File.Exists(source)) { Warn("请先选择有效的源 mastermemory.db。"); return; }
        if (!Directory.Exists(json)) { Warn("工作 JSON 目录不存在。"); return; }
        if (!string.IsNullOrWhiteSpace(baseline) && !Directory.Exists(baseline)) { Warn("baseline JSON 目录不存在。"); return; }
        if (string.IsNullOrWhiteSpace(output)) { Warn("请先选择输出数据库路径。"); return; }

        var requireExact = _exactBox.Checked;
        var baselineOrNull = string.IsNullOrWhiteSpace(baseline) ? null : baseline;
        IProgress<string> progress = new Progress<string>(Append);
        await RunAsync("重打包", async token =>
        {
            var result = await Task.Run(
                () => MasterMemoryDatabaseService.PackFromJson(
                    source, json, baselineOrNull, output, requireExact, progress.Report, token),
                token);

            Append($"表数量：{result.TableCount}，重建：{result.RebuiltTables.Count}，保留：{result.PreservedTables.Count}");
            if (result.RebuiltTables.Count > 0)
                Append("重建的表：" + string.Join(", ", result.RebuiltTables.Take(20))
                    + (result.RebuiltTables.Count > 20 ? $" …（共 {result.RebuiltTables.Count}）" : ""));
            Append($"源   sha256={result.SourceSha256} ({result.SourceLength:N0} 字节)");
            Append($"输出 sha256={result.OutputSha256} ({result.OutputLength:N0} 字节)");
            Append(result.Exact ? "结果：与源数据库逐字节一致。" : "结果：输出已按修改内容重建。");
        });
    }

    private async Task RunAsync(string label, Func<CancellationToken, Task> operation)
    {
        if (_cancellation is not null) return;
        _cancellation = new CancellationTokenSource();
        _exportButton.Enabled = false;
        _packButton.Enabled = false;
        _cancelButton.Enabled = true;
        try
        {
            await operation(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Append($"{label}已取消。");
        }
        catch (Exception exception)
        {
            Append($"{label}失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, $"{label}失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _exportButton.Enabled = true;
            _packButton.Enabled = true;
            _cancelButton.Enabled = false;
        }
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "参数不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void Append(string message) => _log.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
}
