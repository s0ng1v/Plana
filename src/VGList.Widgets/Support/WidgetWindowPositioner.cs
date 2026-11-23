using Avalonia;
using Avalonia.Controls;

namespace VGList.Widgets.Support;

/// <summary>将小组件的附属浮窗（详情/设置）贴合摆放在宿主左或右侧并夹入工作区。</summary>
public static class WidgetWindowPositioner
{
    public static void PlaceBeside(Window anchor, Window child, bool preferRight)
    {
        var scaling = child.RenderScaling > 0 ? child.RenderScaling : anchor.RenderScaling;
        var width = (int)Math.Round(child.Width * scaling);
        var height = (int)Math.Round(child.Height * scaling);
        var gap = (int)Math.Round(12 * scaling);

        var left = anchor.PointToScreen(new Point(0, 0));
        var right = anchor.PointToScreen(new Point(anchor.Bounds.Width, 0));
        var work = (anchor.Screens.ScreenFromPoint(left) ?? anchor.Screens.Primary)?.WorkingArea
                   ?? new PixelRect(0, 0, 1920, 1080);

        var leftX = left.X - width - gap;
        var rightX = right.X + gap;

        int x;
        if (preferRight)
        {
            if (rightX + width <= work.Right)
            {
                x = rightX;
            }
            else if (leftX >= work.X)
            {
                x = leftX;
            }
            else
            {
                x = Math.Clamp(rightX, work.X, Math.Max(work.X, work.Right - width));
            }
        }
        else
        {
            if (leftX >= work.X)
            {
                x = leftX;
            }
            else if (rightX + width <= work.Right)
            {
                x = rightX;
            }
            else
            {
                x = Math.Clamp(rightX, work.X, Math.Max(work.X, work.Right - width));
            }
        }

        var y = left.Y + (int)Math.Round(12 * scaling);
        child.Position = new PixelPoint(
            x,
            Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - height)));
    }
}
