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
            using var home = new ToolboxHomeForm(null);
            var toolLabels = FindControls<Button>(home)
                .Select(static button => button.Text)
                .ToArray();
            if (toolLabels.Count(static text => text.StartsWith("主数据 / CDN 全量同步", StringComparison.Ordinal)) != 1 ||
                toolLabels.Any(static text => text.StartsWith("主数据上传", StringComparison.Ordinal)) ||
                toolLabels.Count(static text => text.Contains("全量同步", StringComparison.Ordinal)) != 1)
            {
                throw new InvalidOperationException("首页 R2 功能入口未正确合并为一个主数据 / CDN 同步入口。");
            }

            using var master = new MasterToolForm(null);
            using var chart = new ChartToolForm();
            using var episode = new EpisodeToolForm();
            using var cache = new SceneAssetCacheForm();
            using var editor = new EpisodeEditorForm();
            using var sync = new R2SyncForm();
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
