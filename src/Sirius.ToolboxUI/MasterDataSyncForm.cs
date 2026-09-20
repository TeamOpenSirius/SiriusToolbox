using System.Drawing;
using System.Windows.Forms;
using Sirius.Toolbox.Master.Sync;

namespace Sirius.ToolboxUI;

/// <summary>
/// 官方同步窗口：MasterData（注册 / 认证 / 下载 / 校验 / 导出）与 CDN 资源镜像（增量）。
/// Official sync window: MasterData (register / authenticate / download / verify / export) and
/// incremental CDN asset mirroring.
/// </summary>
public sealed class MasterDataSyncForm : Form
{
    private readonly TextBox _outputBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _apiBox = new() { Dock = DockStyle.Fill, Text = "https://api.wds-stellarium.com" };
    private readonly TextBox _versionBox = new() { Dock = DockStyle.Fill, Text = "2.30.1" };
    private readonly TextBox _authSuffixBox = new() { Dock = DockStyle.Fill, Text = ".486" };
    private readonly TextBox _registrationNameBox = new() { Dock = DockStyle.Fill, Text = "ToolboxUser" };
    private readonly ComboBox _platformBox = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _gameVersionBox = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 99, Value = 2 };
    private readonly TextBox _fmBox = new() { Dock = DockStyle.Fill, Text = "0" };
    private readonly TextBox _loginTokenBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _accessTokenBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox _categoriesBox = new() { Dock = DockStyle.Fill, Text = "2d-assets,3d-assets,cri-assets" };
    private readonly TextBox _catalogTemplateBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _userAgentBox = new() { Dock = DockStyle.Fill, Text = "BestHTTP/2 v2.8.5" };
    private readonly NumericUpDown _concurrencyBox = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 128, Value = 12 };
    private readonly NumericUpDown _retriesBox = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 20, Value = 5 };
    private readonly NumericUpDown _timeoutBox = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 120, Value = 10 };

    private readonly CheckBox _forceBox = new() { AutoSize = true, Text = "强制重新下载主数据" };
    private readonly CheckBox _exportBox = new() { AutoSize = true, Checked = true, Text = "导出类型化 JSON" };
    private readonly CheckBox _skipMasterDataBox = new() { AutoSize = true, Text = "跳过 MasterData" };
    private readonly CheckBox _skipAssetsBox = new() { AutoSize = true, Text = "跳过 CDN 资源" };
    private readonly CheckBox _skipStaticAssetsBox = new() { AutoSize = true, Text = "跳过 static-assets" };
    private readonly CheckBox _skipScenesBox = new() { AutoSize = true, Checked = true, Text = "跳过剧集场景" };
    private readonly CheckBox _catalogOnlyBox = new() { AutoSize = true, Text = "仅更新 catalog 与清单" };
    private readonly CheckBox _forceAssetsBox = new() { AutoSize = true, Text = "强制重下全部资源对象" };
    private readonly CheckBox _insecureTlsBox = new() { AutoSize = true, Text = "忽略 TLS 证书错误" };

    private readonly Button _syncButton = new() { AutoSize = true, Text = "开始同步" };
    private readonly Button _cancelButton = new() { AutoSize = true, Enabled = false, Text = "取消" };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F) };
    private CancellationTokenSource? _cancellation;

    public MasterDataSyncForm()
    {
        Text = "官方 MasterData / CDN 同步";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(880, 640);
        Size = new Size(1020, 760);
        _platformBox.Items.AddRange(["google-play", "app-store"]);
        _platformBox.SelectedIndex = 0;
        BuildUi();
        _syncButton.Click += async (_, _) => await SyncAsync();
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();
    }

    private void BuildUi()
    {
        var account = BuildGrid();
        AddRow(account, 0, "输出目录：", _outputBox);
        var browse = new Button { AutoSize = true, Text = "选择…" };
        browse.Click += (_, _) => BrowseOutput();
        account.Controls.Add(browse, 2, 0);
        AddRow(account, 1, "官方 API：", _apiBox);
        AddRow(account, 2, "客户端版本：", _versionBox);
        AddRow(account, 3, "认证版本后缀：", _authSuffixBox);
        AddRow(account, 4, "注册名：", _registrationNameBox);
        AddRow(account, 5, "平台：", _platformBox);
        AddRow(account, 6, "GameVersion：", _gameVersionBox);
        AddRow(account, 7, "FM：", _fmBox);
        AddRow(account, 8, "登录令牌：", _loginTokenBox);
        AddRow(account, 9, "访问令牌：", _accessTokenBox);

        var masterOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        masterOptions.Controls.AddRange(
            [_forceBox, _exportBox, _skipMasterDataBox, _skipAssetsBox, _insecureTlsBox]);
        account.Controls.Add(masterOptions, 1, 10);
        account.SetColumnSpan(masterOptions, 2);

        var assets = BuildGrid();
        AddRow(assets, 0, "资源分类：", _categoriesBox);
        AddRow(assets, 1, "catalog 模板：", _catalogTemplateBox);
        AddRow(assets, 2, "并发：", _concurrencyBox);
        AddRow(assets, 3, "重试次数：", _retriesBox);
        AddRow(assets, 4, "超时（分钟）：", _timeoutBox);
        AddRow(assets, 5, "User-Agent：", _userAgentBox);

        var assetOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        assetOptions.Controls.AddRange(
            [_skipStaticAssetsBox, _skipScenesBox, _catalogOnlyBox, _forceAssetsBox]);
        assets.Controls.Add(assetOptions, 1, 6);
        assets.SetColumnSpan(assetOptions, 2);

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(8, 8, 8, 4) };
        var accountTab = new TabPage("账号与主数据") { AutoScroll = true };
        var assetTab = new TabPage("CDN 资源") { AutoScroll = true };
        accountTab.Controls.Add(account);
        assetTab.Controls.Add(assets);
        tabs.TabPages.Add(accountTab);
        tabs.TabPages.Add(assetTab);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(12, 4, 12, 8)
        };
        actions.Controls.Add(_syncButton);
        actions.Controls.Add(_cancelButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 440));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(tabs, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(_log, 0, 2);
        Controls.Add(layout);
    }

    private static TableLayoutPanel BuildGrid()
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
        return grid;
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.Controls.Add(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Text = label }, 0, row);
        panel.Controls.Add(control, 1, row);
    }

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog
        {
            UseDescriptionForTitle = true,
            Description = "选择同步输出目录（CDN 内容会直接写入此目录，不会再创建 assets 子目录）"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputBox.Text = dialog.SelectedPath;
    }

    private async Task SyncAsync()
    {
        if (_cancellation is not null)
            return;
        _cancellation = new CancellationTokenSource();
        _syncButton.Enabled = false;
        _cancelButton.Enabled = true;
        try
        {
            var options = new MasterDataSyncOptions(_outputBox.Text)
            {
                ApiBootstrapUrl = _apiBox.Text.Trim(),
                ApplicationVersion = _versionBox.Text.Trim(),
                AuthenticationVersionSuffix = EmptyToNull(_authSuffixBox.Text) ?? ".486",
                RegistrationName = _registrationNameBox.Text.Trim(),
                Platform = _platformBox.SelectedItem as string ?? "google-play",
                GameVersion = (int)_gameVersionBox.Value,
                Fm = _fmBox.Text.Trim(),
                LoginToken = EmptyToNull(_loginTokenBox.Text),
                AccessToken = EmptyToNull(_accessTokenBox.Text),
                Force = _forceBox.Checked,
                ExportJson = _exportBox.Checked,
                InsecureTls = _insecureTlsBox.Checked,
                SkipMasterData = _skipMasterDataBox.Checked,
                SkipAssets = _skipAssetsBox.Checked,
                SkipStaticAssets = _skipStaticAssetsBox.Checked,
                SkipScenes = _skipScenesBox.Checked,
                CatalogOnly = _catalogOnlyBox.Checked,
                ForceAssets = _forceAssetsBox.Checked,
                AssetCategories = ParseCategories(_categoriesBox.Text),
                CatalogTemplate = EmptyToNull(_catalogTemplateBox.Text),
                AssetConcurrency = (int)_concurrencyBox.Value,
                AssetRetries = (int)_retriesBox.Value,
                AssetTimeoutMinutes = (int)_timeoutBox.Value,
                AssetUserAgent = _userAgentBox.Text.Trim()
            };
            var progress = new Progress<MasterDataSyncProgress>(x => Append($"[{x.Stage}] {x.Message}"));
            var result = await new MasterDataSyncService().SyncAsync(options, progress, _cancellation.Token);
            Append($"完成：MasterData {result.Version}，数据库 {result.DatabasePath}");
            if (result.Verification is { } verification)
                Append($"校验：{verification.TableCount} 表 / {verification.RowCount:N0} 行 / sha256={verification.Sha256}");
            if (result.Assets is { } assets)
                Append($"资源：对象 {assets.ObjectCount}，本次下载 {assets.DownloadedCount}，404 {assets.NotFoundCount}，移除 {assets.RemovedCount}，catalog {assets.CatalogCount}");
            Append($"发布清单：{result.PublicationPath}");
        }
        catch (OperationCanceledException)
        {
            Append("已取消。");
        }
        catch (Exception exception)
        {
            Append($"失败：{exception.Message}");
            MessageBox.Show(this, exception.Message, "同步失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _syncButton.Enabled = true;
            _cancelButton.Enabled = false;
        }
    }

    private static IReadOnlyList<string> ParseCategories(string value) =>
        [.. value
            .Split([',', ';', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private void Append(string message) => _log.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
