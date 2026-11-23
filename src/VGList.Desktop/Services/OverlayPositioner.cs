using Avalonia;
using Avalonia.Controls;

namespace VGList.Desktop.Services;

public enum OverlayPlacement
{
    BelowLeftAligned,
    BelowRightAligned,
    AboveLeftAligned,
    BesideRight,
    BesideLeft,
    BesidePreferRight,
    Center,
}

/// <summary>窗内 Canvas 浮层统一定位与进入动效辅助，避免使用原生 Popup 产生矩形边缘伪影。</summary>
public static class OverlayPositioner
{
    public static void PlaceAndShow(
        Control host,
        Control overlay,
        Control? anchor,
        OverlayPlacement placement,
        double gap = 6,
        double edgeMargin = 10)
    {
        var hostWidth = host.Bounds.Width > 0 ? host.Bounds.Width : 900;
        var hostHeight = host.Bounds.Height > 0 ? host.Bounds.Height : 640;

        overlay.Measure(new Size(hostWidth, hostHeight));
        var cardWidth = overlay.DesiredSize.Width > 0 ? overlay.DesiredSize.Width : 284;
        var cardHeight = overlay.DesiredSize.Height > 0 ? overlay.DesiredSize.Height : 240;

        var anchorPos = anchor?.TranslatePoint(new Point(0, 0), host) ?? new Point(hostWidth / 2, hostHeight / 2);
        var anchorW = anchor?.Bounds.Width ?? 0;
        var anchorH = anchor?.Bounds.Height ?? 0;

        double left;
        double top;
        double enterX = 0;
        double enterY = -6;

        switch (placement)
        {
            case OverlayPlacement.BelowRightAligned:
                left = anchorPos.X + anchorW - cardWidth;
                top = anchorPos.Y + anchorH + gap;
                break;

            case OverlayPlacement.AboveLeftAligned:
                left = anchorPos.X;
                top = anchorPos.Y - cardHeight - gap;
                enterY = 6;
                break;

            case OverlayPlacement.BesideRight:
                left = anchorPos.X + anchorW + gap;
                top = anchorPos.Y;
                enterX = -8;
                enterY = 0;
                break;

            case OverlayPlacement.BesideLeft:
                left = anchorPos.X - cardWidth - gap;
                top = anchorPos.Y;
                enterX = 8;
                enterY = 0;
                break;

            case OverlayPlacement.BesidePreferRight:
                if (anchorPos.X + anchorW + gap + cardWidth + edgeMargin <= hostWidth)
                {
                    left = anchorPos.X + anchorW + gap;
                    enterX = -8;
                }
                else
                {
                    left = anchorPos.X - cardWidth - gap;
                    enterX = 8;
                }

                top = anchorPos.Y;
                enterY = 0;
                break;

            case OverlayPlacement.Center:
                left = (hostWidth - cardWidth) / 2.0;
                top = (hostHeight - cardHeight) / 2.0;
                enterY = 8;
                break;

            default:
                left = anchorPos.X;
                top = anchorPos.Y + anchorH + gap;
                break;
        }

        var maxLeft = Math.Max(edgeMargin, hostWidth - cardWidth - edgeMargin);
        var maxTop = Math.Max(edgeMargin, hostHeight - cardHeight - edgeMargin);
        Canvas.SetLeft(overlay, Math.Clamp(left, edgeMargin, maxLeft));
        Canvas.SetTop(overlay, Math.Clamp(top, edgeMargin, maxTop));

        MotionHelper.PlayEnter(overlay, enterX, enterY, 160);
    }
}
