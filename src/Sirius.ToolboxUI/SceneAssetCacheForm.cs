using System.Drawing;
using System.Windows.Forms;
using Sirius.Toolbox.Episodes;

namespace Sirius.ToolboxUI;

public sealed class SceneAssetCacheForm : Form
{
    private readonly TextBox _episodeDirectoryBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _sceneDirectoryBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _outputBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _masterDataVersionBox = new() { Dock = DockStyle.Fill, PlaceholderText = "可留空" };
    private readonly TextBox _sourceRevisionBox = new() { Dock = DockStyle.Fill, PlaceholderText = "可留空" };
    private readonly TextBox _episodePathPrefixBox = new() { Dock = DockStyle.Fill, Text = "episode" };
    private readonly TextBox _scenePathPrefixBox = new() { Dock = DockStyle.Fill, Text = "scenes" };
    private readonly CheckBox _metadataOnlyBox = new() { AutoSize = true, Text = "只记录文件元数据（不计算 SHA-256）", Checked = true };
    private readonly CheckBox _overwriteBox = new() { AutoSize = true, Text = "允许覆盖已有缓存" };
    private readonly Button _buildButton = new() { AutoSize = true, Text = "建立缓存" };
    private readonly Button _openButton = new() { AutoSize = true, Text = "打开缓存" };
    private readonly DataGridView _grid = new()
    {
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText,
        Dock = DockStyle.Fill,
        ReadOnly = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly RichTextBox _logBox = new()
    {
        BackColor = SystemColors.Window,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9F),
        ReadOnly = true,
        WordWrap = false
    };

    private bool _busy;

    public SceneAssetCacheForm()
    {
        Text = "剧情资源缓存";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 650);
        Size = new Size(1280, 820);

        BuildUi();
        _buildButton.Click += async (_, _) => await BuildCacheAsync();
        _openButton.Click += async (_, _) => await OpenCacheAsync();
    }

    private void BuildUi()
    {
        AddColumn("剧情 ID", 105);
        AddColumn("场景相对路径", 260);
        AddColumn("文件名", 145);
        AddColumn("JSON 来源", 270);
        AddColumn("大小", 100);
        AddColumn("SHA-256 哈希", 530);
        AddColumn("模式", 110);

        var episodeBrowse = new Button { AutoSize = true, Text = "选择目录..." };
        episodeBrowse.Click += (_, _) => BrowseFolder(_episodeDirectoryBox, "选择剧情 JSON 目录");
        var sceneBrowse = new Button { AutoSize = true, Text = "选择目录..." };
        sceneBrowse.Click += (_, _) => BrowseFolder(_sceneDirectoryBox, "选择场景 BIN 目录");
        var outputBrowse = new Button { AutoSize = true, Text = "选择文件..." };
        outputBrowse.Click += (_, _) => BrowseOutput();

        var form = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            RowCount = 9
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPathRow(form, 0, "剧情 JSON 目录：", _episodeDirectoryBox, episodeBrowse);
        AddPathRow(form, 1, "场景 BIN 目录：", _sceneDirectoryBox, sceneBrowse);
        AddPathRow(form, 2, "缓存输出文件：", _outputBox, outputBrowse);
        form.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "主数据版本：" }, 0, 3);
        form.Controls.Add(_masterDataVersionBox, 1, 3);
        form.SetColumnSpan(_masterDataVersionBox, 2);
        form.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "来源修订号：" }, 0, 4);
        form.Controls.Add(_sourceRevisionBox, 1, 4);
        form.SetColumnSpan(_sourceRevisionBox, 2);
        form.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "剧情 JSON CDN 前缀：" }, 0, 5);
        form.Controls.Add(_episodePathPrefixBox, 1, 5);
        form.SetColumnSpan(_episodePathPrefixBox, 2);
        form.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "场景 BIN CDN 前缀：" }, 0, 6);
        form.Controls.Add(_scenePathPrefixBox, 1, 6);
        form.SetColumnSpan(_scenePathPrefixBox, 2);

        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        options.Controls.Add(_metadataOnlyBox);
        options.Controls.Add(_overwriteBox);
        options.Controls.Add(_buildButton);
        options.Controls.Add(_openButton);
        form.Controls.Add(options, 0, 7);
        form.SetColumnSpan(options, 3);

        var help = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "SourcePath 和 RelativePath 使用可自定义的 CDN 前缀；重复 Episode ID 会拒绝输出。"
        };
        form.Controls.Add(help, 0, 8);
        form.SetColumnSpan(help, 3);

        var view = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2
        };
        view.Resize += (_, _) => ConfigureViewSplitter(view);
        view.Panel1.Controls.Add(_grid);
        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        logPanel.Controls.Add(_logBox);
        logPanel.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Top, Text = "运行日志" });
        view.Panel2.Controls.Add(logPanel);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 320));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(form, 0, 0);
        root.Controls.Add(view, 0, 1);
        Controls.Add(root);
    }

    private static void ConfigureViewSplitter(SplitContainer split)
    {
        const int topMinimum = 180;
        const int bottomMinimum = 130;
        if (split.Height < topMinimum + bottomMinimum)
            return;

        split.Panel1MinSize = topMinimum;
        split.Panel2MinSize = bottomMinimum;
        split.SplitterDistance = Math.Clamp(360, topMinimum, split.Height - bottomMinimum);
    }

    private void AddColumn(string title, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = title,
            HeaderText = title,
            Name = title,
            Width = width
        });
    }

    private static void AddPathRow(TableLayoutPanel form, int row, string label, TextBox box, Button browse)
    {
        form.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = label }, 0, row);
        form.Controls.Add(box, 1, row);
        form.Controls.Add(browse, 2, row);
    }

    private static void BrowseFolder(TextBox target, string description)
    {
        using var dialog = new FolderBrowserDialog { Description = description };
        if (dialog.ShowDialog() == DialogResult.OK)
            target.Text = dialog.SelectedPath;
    }

    private void BrowseOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            FileName = "scene-assets.json"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputBox.Text = dialog.FileName;
    }

    private async Task BuildCacheAsync()
    {
        if (_busy) return;
        try
        {
            var episodeDirectory = RequireDirectory(_episodeDirectoryBox.Text, "请选择剧情 JSON 目录。");
            var sceneDirectory = RequireDirectory(_sceneDirectoryBox.Text, "请选择场景 BIN 目录。");
            var outputPath = RequirePath(_outputBox.Text, "请选择缓存输出文件。");
            EnsureOutput(outputPath);
            SetBusy(true);
            AppendLog("开始建立剧情资源缓存...");

            var options = new SceneAssetCacheOptions(
                _metadataOnlyBox.Checked,
                NullIfEmpty(_masterDataVersionBox.Text),
                NullIfEmpty(_sourceRevisionBox.Text),
                NullIfEmpty(_episodePathPrefixBox.Text) ?? "episode",
                NullIfEmpty(_scenePathPrefixBox.Text) ?? "scenes");
            var result = await Task.Run(() => new SceneAssetCacheService().Build(
                episodeDirectory,
                sceneDirectory,
                outputPath,
                options,
                _overwriteBox.Checked));
            var document = await Task.Run(() => new SceneAssetCacheService().Load(result.OutputPath));
            BindDocument(document);
            AppendLog($"完成：{result.AssetCount} 个资源，匹配 JSON {result.MatchedJsonCount} 个，缺失 JSON {result.MissingJsonCount} 个，总计 {result.TotalBytes} 字节。");
            AppendLog($"输出：{result.OutputPath}");
        }
        catch (Exception exception)
        {
            AppendLog($"失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "剧情资源缓存错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenCacheAsync()
    {
        if (_busy) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "缓存 JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            Title = "打开剧情资源缓存"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            SetBusy(true);
            var path = dialog.FileName;
            var document = await Task.Run(() => new SceneAssetCacheService().Load(path));
            BindDocument(document);
            _outputBox.Text = path;
            _masterDataVersionBox.Text = document.MasterDataVersion;
            _sourceRevisionBox.Text = document.SourceRevision;
            _metadataOnlyBox.Checked = document.Assets.Values.All(static item => item.MetadataOnly);
            AppendLog($"已打开：{path}，{document.Assets.Count} 个资源，生成时间 {document.GeneratedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}。");
        }
        catch (Exception exception)
        {
            AppendLog($"打开失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "打开缓存错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BindDocument(SceneAssetCacheDocument document)
    {
        _grid.Rows.Clear();
        foreach (var pair in document.Assets.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var entry = pair.Value;
            _grid.Rows.Add(
                pair.Key,
                entry.RelativePath,
                entry.FileName,
                entry.SourcePath,
                entry.FileSize.ToString("N0"),
                entry.Sha256,
                entry.MetadataOnly ? "元数据" : entry.HashAlgorithm);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _buildButton.Enabled = !busy;
        _openButton.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void EnsureOutput(string path)
    {
        if (File.Exists(path) && !_overwriteBox.Checked)
            throw new IOException($"输出文件已存在：{path}。请勾选“允许覆盖已有缓存”。");
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
    }

    private static string RequireDirectory(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message);
        var path = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"找不到目录：{path}");
        return path;
    }

    private static string RequirePath(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message);
        return Path.GetFullPath(value.Trim());
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void AppendLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
