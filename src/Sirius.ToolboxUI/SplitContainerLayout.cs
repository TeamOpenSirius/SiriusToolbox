using System.Windows.Forms;

namespace Sirius.ToolboxUI;

internal static class SplitContainerLayout
{
    public static void ConfigureWhenSized(
        SplitContainer split,
        Orientation orientation,
        int panel1MinSize,
        int panel2MinSize,
        int desiredDistance)
    {
        var configured = false;

        void Configure()
        {
            if (configured) return;

            var size = orientation == Orientation.Horizontal ? split.Height : split.Width;
            var minimumSize = panel1MinSize + panel2MinSize + split.SplitterWidth;
            if (size < minimumSize) return;

            split.Panel1MinSize = panel1MinSize;
            split.Panel2MinSize = panel2MinSize;
            var maximumDistance = size - panel2MinSize - split.SplitterWidth;
            split.SplitterDistance = Math.Clamp(desiredDistance, panel1MinSize, maximumDistance);
            configured = true;
        }

        split.SizeChanged += (_, _) => Configure();
        Configure();
    }
}
