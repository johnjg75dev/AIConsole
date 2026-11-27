using System.Windows;
using AIConsole.Models;
using AIConsole.Services;

namespace AIConsole.Tests;

public class WindowPlacementTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1080);

    [Fact]
    public void LeftEdgeAtZero_IsTopLeftOfWorkArea()
    {
        var (left, top) = WindowPlacement.Compute(SlideEdge.Left, Work, 880, 600, 0, 0, 0);

        Assert.Equal(0, left);
        Assert.Equal(0, top);
    }

    [Fact]
    public void OffsetsMoveRightAndDown()
    {
        var (left, top) = WindowPlacement.Compute(SlideEdge.Left, Work, 880, 600, 0, 50, 20);

        Assert.Equal(50, left);
        Assert.Equal(20, top);
    }

    [Fact]
    public void RightEdge_IsFlushRightByDefault()
    {
        var (left, top) = WindowPlacement.Compute(SlideEdge.Right, Work, 880, 600, 0, 0, 0);

        Assert.Equal(1040, left);
        Assert.Equal(0, top);
    }

    [Fact]
    public void TopEdgeFullWidth_StartsAtWorkAreaLeft()
    {
        var (left, top) = WindowPlacement.Compute(SlideEdge.Top, Work, 1920, 600, 0, 0, 0);

        Assert.Equal(0, left);
        Assert.Equal(0, top);
    }

    [Fact]
    public void TopEdgeCustomWidth_IsCentered()
    {
        var (left, _) = WindowPlacement.Compute(SlideEdge.Top, Work, 880, 600, 880, 0, 0);

        Assert.Equal(520, left);
    }

    [Fact]
    public void OffsetsAreClampedToKeepPanelReachable()
    {
        var (left, _) = WindowPlacement.Compute(SlideEdge.Left, Work, 880, 600, 0, 5000, 0);
        var (_, top) = WindowPlacement.Compute(SlideEdge.Left, Work, 880, 600, 0, 0, 5000);

        Assert.Equal(1840, left);
        Assert.Equal(1000, top);
    }
}
