using System.Drawing;
using System.Windows.Forms;
using Sirius.AssetTool.R2;

namespace Sirius.ToolboxUI;

public sealed class R2SyncForm : Form
{
    private readonly TextBox _rootDirectoryBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _endpointBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _bucketBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _prefixBox = new() { Dock = DockStyle.Fill, PlaceholderText = "可留空" };
    private readonly TextBox _accessKeyBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _secretKeyBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _sessionTokenBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Button _rootBrowseButton = new() { AutoSize = true, Text = "选择目录..." };
    private readonly NumericUpDown _concurrencyBox = new()
    {
        Maximum = 128,
        Minimum = 1,
        Value = 16,
        Width = 90
    };
    private readonly NumericUpDown _retriesBox = new()
    {
        Maximum = 20,
        Minimum = 0,
        Value = MasterDataR2UploadDefaults.MaxRetries,
        Width = 90
    };
    private readonly CheckBox _forceBox = new() { AutoSize = true, Text = "强制上传（不检查远端版本）" };
    private readonly CheckBox _dryRunBox = new() { AutoSize = true, Checked = true, Text = "仅预览，不访问 R2" };
    private readonly Button _scanButton = new() { AutoSize = true, Text = "扫描映射" };
    private readonly Button _syncButton = new() { AutoSize = true, Text = "开始同步" };
    private readonly Button _cancelButton = new() { AutoSize = true, Enabled = false, Text = "取消" };
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "尚未扫描" };
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

    private CancellationTokenSource? _cancellation;
    private bool _busy;

    public R2SyncForm()
    {
        Text = "主数据 / CDN / R2 全量同步";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1100, 760);
        Size = new Size(1420, 920);

        _endpointBox.Text = MasterDataR2UploadDefaults.Endpoint;
        _bucketBox.Text = MasterDataR2UploadDefaults.Bucket;
        _accessKeyBox.Text = Environment.GetEnvironmentVariable("R2_ACCESS_KEY_ID") ?? string.Empty;
        _secretKeyBox.Text = Environment.GetEnvironmentVariable("R2_SECRET_ACCESS_KEY") ?? string.Empty;
        _sessionTokenBox.Text = Environment.GetEnvironmentVariable("R2_SESSION_TOKEN") ?? string.Empty;

        BuildUi();
        _rootBrowseButton.Click += (_, _) => BrowseRootDirectory();
        _scanButton.Click += async (_, _) => await ScanAsync();
        _syncButton.Click += async (_, _) => await SyncAsync();
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();
    }

    private void BuildUi()
    {
        AddColumn("本地路径", 410);
        AddColumn("R2 对象键", 520);
        AddColumn("大小", 110);
        AddColumn("类型", 220);
        AddColumn("编码", 110);

        var settings = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 11
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var row = 0; row < 10; row++)
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddPathRow(settings, 0, "同步输出目录：", _rootDirectoryBox, _rootBrowseButton);
        AddValueRow(settings, 1, "R2 S3 地址：", _endpointBox);
        AddValueRow(settings, 2, "存储桶：", _bucketBox);
        AddValueRow(settings, 3, "对象键前缀：", _prefixBox);
        AddValueRow(settings, 4, "访问密钥 ID：", _accessKeyBox);
        AddValueRow(settings, 5, "秘密访问密钥：", _secretKeyBox);
        AddValueRow(settings, 6, "临时凭据令牌：", _sessionTokenBox);

        settings.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "并发上传数：" }, 0, 7);
        settings.Controls.Add(_concurrencyBox, 1, 7);
        settings.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = "失败重试次数：" }, 0, 8);
        settings.Controls.Add(_retriesBox, 1, 8);

        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        options.Controls.Add(_dryRunBox);
        options.Controls.Add(_forceBox);
        options.Controls.Add(_scanButton);
        options.Controls.Add(_syncButton);
        options.Controls.Add(_cancelButton);
        settings.Controls.Add(options, 0, 9);
        settings.SetColumnSpan(options, 3);

        var help = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "同步 master、assets\\catalogs、assets\\files；哈希缓存：assets\\r2-hash-cache.json；预览映射：assets\\r2-object-map.tsv。",
            TextAlign = ContentAlignment.MiddleLeft
        };
        settings.Controls.Add(help, 0, 10);
        settings.SetColumnSpan(help, 2);
        settings.Controls.Add(_statusLabel, 2, 10);

        var gridGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            Text = "对象映射"
        };
        gridGroup.Controls.Add(_grid);

        var logGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            Text = "运行日志"
        };
        logGroup.Controls.Add(_logBox);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 360));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        layout.Controls.Add(settings, 0, 0);
        layout.Controls.Add(gridGroup, 0, 1);
        layout.Controls.Add(logGroup, 0, 2);
        Controls.Add(layout);
    }

    private void AddColumn(string title, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            Name = title,
            Width = width
        });
    }

    private static void AddPathRow(TableLayoutPanel table, int row, string label, TextBox box, Button browse)
    {
        table.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = label }, 0, row);
        table.Controls.Add(box, 1, row);
        table.Controls.Add(browse, 2, row);
    }

    private static void AddValueRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = label }, 0, row);
        table.Controls.Add(control, 1, row);
        table.SetColumnSpan(control, 2);
    }

    private void BrowseRootDirectory()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择 CDN 同步输出目录" };
        if (Directory.Exists(_rootDirectoryBox.Text))
            dialog.SelectedPath = Path.GetFullPath(_rootDirectoryBox.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _rootDirectoryBox.Text = dialog.SelectedPath;
    }

    private async Task ScanAsync()
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            var options = ReadOptions(dryRun: true);
            var plan = await Task.Run(() => new R2AssetSyncService().BuildPlan(options));
            BindPlan(plan);
            _statusLabel.Text = $"{plan.Objects.Count} 个对象，{plan.TotalBytes:N0} 字节";
            AppendLog($"扫描完成：{plan.Objects.Count} 个对象，共 {plan.TotalBytes:N0} 字节。");
        }
        catch (Exception exception)
        {
            AppendLog($"扫描失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "R2 扫描错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SyncAsync()
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            using var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            var options = ReadOptions(_dryRunBox.Checked);
            AppendLog(options.DryRun ? "开始预览主数据 / CDN / R2 全量映射..." : "开始主数据 / CDN / R2 全量同步...");
            var lastMessage = string.Empty;
            var progress = new Progress<R2SyncProgress>(item =>
            {
                _statusLabel.Text = item.Message;
                if (!string.Equals(lastMessage, item.Message, StringComparison.Ordinal))
                {
                    lastMessage = item.Message;
                    if (item.Completed == item.Total || item.Message.StartsWith("警告", StringComparison.Ordinal))
                        AppendLog(item.Message);
                }
            });
            var result = await Task.Run(
                () => new R2AssetSyncService().SyncAsync(options, progress, cancellation.Token),
                cancellation.Token);
            if (result.DryRun)
            {
                AppendLog($"预览完成：映射清单 {result.MappingManifestPath}");
                var plan = await Task.Run(() => new R2AssetSyncService().BuildPlan(options));
                BindPlan(plan);
                _statusLabel.Text = $"{plan.Objects.Count} 个对象，预览完成";
            }
            else
            {
                AppendLog($"同步完成：上传 {result.UploadedCount}，跳过 {result.SkippedCount}，已发送 {result.UploadedBytes:N0} 字节。");
                AppendLog($"哈希缓存：{result.HashCachePath}");
                _statusLabel.Text = $"完成：上传 {result.UploadedCount}，跳过 {result.SkippedCount}";
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("已取消。");
            _statusLabel.Text = "已取消";
        }
        catch (Exception exception)
        {
            AppendLog($"同步失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "R2 同步错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation = null;
            SetBusy(false);
        }
    }

    private R2SyncOptions ReadOptions(bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(_rootDirectoryBox.Text))
            throw new ArgumentException("请选择同步输出目录。");
        return new R2SyncOptions(Path.GetFullPath(_rootDirectoryBox.Text.Trim()))
        {
            Endpoint = _endpointBox.Text.Trim(),
            Bucket = _bucketBox.Text.Trim(),
            KeyPrefix = _prefixBox.Text.Trim(),
            AccessKeyId = NullIfEmpty(_accessKeyBox.Text),
            SecretAccessKey = NullIfEmpty(_secretKeyBox.Text),
            SessionToken = NullIfEmpty(_sessionTokenBox.Text),
            Concurrency = (int)_concurrencyBox.Value,
            MaxRetries = (int)_retriesBox.Value,
            Force = _forceBox.Checked,
            DryRun = dryRun
        };
    }

    private void BindPlan(R2SyncPlan plan)
    {
        _grid.Rows.Clear();
        foreach (var item in plan.Objects)
        {
            _grid.Rows.Add(
                item.LocalPath,
                item.ObjectKey,
                item.Length.ToString("N0"),
                item.ContentType,
                item.ContentEncoding ?? string.Empty);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _rootDirectoryBox.Enabled = !busy;
        _rootBrowseButton.Enabled = !busy;
        _endpointBox.Enabled = !busy;
        _bucketBox.Enabled = !busy;
        _prefixBox.Enabled = !busy;
        _accessKeyBox.Enabled = !busy;
        _secretKeyBox.Enabled = !busy;
        _sessionTokenBox.Enabled = !busy;
        _concurrencyBox.Enabled = !busy;
        _retriesBox.Enabled = !busy;
        _forceBox.Enabled = !busy;
        _dryRunBox.Enabled = !busy;
        _scanButton.Enabled = !busy;
        _syncButton.Enabled = !busy;
        _cancelButton.Enabled = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void AppendLog(string message)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
