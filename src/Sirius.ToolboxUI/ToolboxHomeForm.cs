using System.Drawing;
using System.Windows.Forms;

namespace Sirius.ToolboxUI;

public sealed class ToolboxHomeForm : Form
{
    private string? _startupPath;
    private readonly ToolboxWindowRegistry _windows;
    private bool _isClosing;

    public ToolboxHomeForm(string? startupPath)
    {
        _startupPath = startupPath;
        _windows = new ToolboxWindowRegistry(this);

        Text = "Sirius 工具箱";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 560);
        Size = new Size(860, 680);

        BuildUi();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!string.IsNullOrWhiteSpace(_startupPath) && File.Exists(_startupPath))
        {
            var startupPath = _startupPath;
            _startupPath = null;
            OpenMasterTool(startupPath);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _isClosing = true;
        _windows.CloseAll();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var title = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Sirius 工具箱",
            TextAlign = ContentAlignment.MiddleCenter
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "请选择要使用的工具",
            TextAlign = ContentAlignment.MiddleCenter
        };

        var masterButton = CreateToolButton("主数据编辑器", "浏览、编辑和校验 MasterMemory 数据库");
        masterButton.Click += (_, _) => OpenMasterTool(null);
        var chartButton = CreateToolButton("谱面工具", "转换、编码和解码谱面文件");
        chartButton.Click += (_, _) => OpenChartTool();
        var episodeButton = CreateToolButton("剧情工具", "打包、解包和检查剧情文件");
        episodeButton.Click += (_, _) => OpenEpisodeTool();
        var cacheButton = CreateToolButton("剧情资源缓存", "建立和查看 scene-assets.json 缓存");
        cacheButton.Click += (_, _) => OpenSceneAssetCache();
        var editorButton = CreateToolButton("剧情编辑器", "编辑剧情 JSON 并导出 BIN");
        editorButton.Click += (_, _) => OpenEpisodeEditor();
        var syncButton = CreateToolButton("主数据 / CDN 全量同步", "同步 MasterData、目录清单和 CDN 资源");
        syncButton.Click += (_, _) => OpenR2Sync();

        var buttons = new TableLayoutPanel
        {
            AutoSize = false,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 2
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        buttons.RowStyles.Clear();
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        buttons.Controls.Add(masterButton, 0, 0);
        buttons.Controls.Add(chartButton, 1, 0);
        buttons.Controls.Add(episodeButton, 2, 0);
        buttons.Controls.Add(cacheButton, 0, 1);
        buttons.Controls.Add(editorButton, 1, 1);
        buttons.Controls.Add(syncButton, 2, 1);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
    }

    private static Button CreateToolButton(string title, string description)
    {
        return new Button
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(10),
            Text = $"{title}\r\n{description}",
            UseVisualStyleBackColor = true
        };
    }

    private MasterToolForm OpenMasterTool(string? startupPath)
        => OpenChild("master", () => new MasterToolForm(startupPath));

    private ChartToolForm OpenChartTool()
        => OpenChild("chart", static () => new ChartToolForm());

    private EpisodeToolForm OpenEpisodeTool()
        => OpenChild("episode", static () => new EpisodeToolForm());

    private SceneAssetCacheForm OpenSceneAssetCache()
        => OpenChild("scene-cache", static () => new SceneAssetCacheForm());

    private EpisodeEditorForm OpenEpisodeEditor()
        => OpenChild("episode-editor", static () => new EpisodeEditorForm());

    private R2SyncForm OpenR2Sync()
        => OpenChild("r2-sync", static () => new R2SyncForm());

    private T OpenChild<T>(string key, Func<T> factory) where T : Form
    {
        var window = _windows.OpenOrActivate(key, factory, ShowHome);
        Hide();
        return window;
    }

    private void ShowHome()
    {
        if (_isClosing || IsDisposed) return;
        Show();
        Activate();
    }
}
