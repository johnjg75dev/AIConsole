using AIConsole.Models;
using System.Windows;

namespace AIConsole.Services;

/// <summary>Pure placement math for the slide-out panel, testable without a window.</summary>
public static class WindowPlacement
{
    private const double KeepVisible = 80;

    public static (double Left, double Top) Compute(
        SlideEdge edge, Rect workArea, double width, double height,
        double customWidth, double offsetX, double offsetY)
    {
        double left = edge switch
        {
            SlideEdge.Right => workArea.Right - width,
            SlideEdge.Top when customWidth > 0 => workArea.Left + (workArea.Width - width) / 2,
            _ => workArea.Left,
        };
        var top = workArea.Top;

        left += offsetX;
        top += offsetY;

        // Keep at least a strip of the panel reachable.
        left = Math.Clamp(left, workArea.Left - width + KeepVisible, workArea.Right - KeepVisible);
        top = Math.Clamp(top, workArea.Top - height + KeepVisible, workArea.Bottom - KeepVisible);
        return (left, top);
    }
}
