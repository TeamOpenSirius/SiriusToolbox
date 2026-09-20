using System.Windows.Forms;

namespace Sirius.ToolboxUI;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Any(static arg => string.Equals(arg, "--self-test-ui", StringComparison.OrdinalIgnoreCase)))
        {
            R2SettingsStore.VerifyProtectionRoundTrip();
            using var home = new ToolboxHomeForm(null);
            var toolLabels = FindControls<Button>(home)
                .Select(static button => button.Text)
                .ToArray();
            if (toolLabels.Count(static text => text.StartsWith("主数据 / CDN 全量同步", StringComparison.Ordinal)) != 1 ||
                toolLabels.Count(static text => text.StartsWith("官方 MasterData / CDN 同步", StringComparison.Ordinal)) != 1 ||
                toolLabels.Count(static text => text.StartsWith("主数据离线重打包", StringComparison.Ordinal)) != 1 ||
                toolLabels.Any(static text => text.StartsWith("主数据上传", StringComparison.Ordinal)) ||
                toolLabels.Count(static text => text.Contains("全量同步", StringComparison.Ordinal)) != 1)
            {
                throw new InvalidOperationException(
                    "首页必须同时提供“主数据 / CDN 全量同步”（本地发布到 R2）与“官方 MasterData / CDN 同步”（官方下载）入口。");
            }

            using var master = new MasterToolForm(null);
            using var chart = new ChartToolForm();
            using var episode = new EpisodeToolForm();
            using var cache = new SceneAssetCacheForm();
            var cacheLabels = FindControls<Label>(cache)
                .Select(static label => label.Text)
                .ToArray();
            if (!cacheLabels.Contains("剧情 JSON CDN 前缀：", StringComparer.Ordinal) ||
                !cacheLabels.Contains("场景 BIN CDN 前缀：", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("剧情资源缓存窗口缺少 CDN 路径前缀配置。");
            }

            using var editor = new EpisodeEditorForm();
            using var sync = new R2SyncForm();
            using var masterSync = new MasterDataSyncForm();
            var masterSyncLayout = masterSync.Controls.OfType<TableLayoutPanel>().SingleOrDefault();
            if (masterSyncLayout is null ||
                masterSyncLayout.RowCount != 3 ||
                !masterSyncLayout.Controls.OfType<TabControl>().Any() ||
                !masterSyncLayout.Controls.OfType<FlowLayoutPanel>().Any() ||
                !masterSyncLayout.Controls.OfType<RichTextBox>().Any())
            {
                throw new InvalidOperationException("官方同步窗口必须使用互不重叠的选项、操作和日志三段式布局。");
            }
            var syncLabels = FindControls<Label>(masterSync)
                .Select(static label => label.Text)
                .ToArray();
            if (!syncLabels.Contains("资源分类：", StringComparer.Ordinal) ||
                !syncLabels.Contains("catalog 模板：", StringComparer.Ordinal) ||
                !syncLabels.Contains("User-Agent：", StringComparer.Ordinal) ||
                !syncLabels.Contains("认证版本后缀：", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("官方同步窗口缺少 CDN 资源镜像选项。");
            }

            using var repack = new MasterMemoryRepackForm();
            var repackLabels = FindControls<Label>(repack)
                .Select(static label => label.Text)
                .ToArray();
            if (!repackLabels.Contains("工作 JSON 目录：", StringComparer.Ordinal) ||
                !repackLabels.Contains("baseline JSON 目录：", StringComparer.Ordinal) ||
                !repackLabels.Contains("输出数据库：", StringComparer.Ordinal))
            {
                throw new InvalidOperationException("离线重打包窗口缺少源数据库、工作 JSON、baseline JSON 或输出路径配置。");
            }
            return 0;
        }

        Application.Run(new ToolboxHomeForm(args.FirstOrDefault()));
        return 0;
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            if (control is T typed)
                yield return typed;
            foreach (var nested in FindControls<T>(control))
                yield return nested;
        }
    }
}
