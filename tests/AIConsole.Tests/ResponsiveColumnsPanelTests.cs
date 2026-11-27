using System.Windows;
using System.Windows.Controls;
using AIConsole.Views.Controls;

namespace AIConsole.Tests;

public class ResponsiveColumnsPanelTests
{
    [Fact]
    public void WideLayout_UsesTwoColumns()
    {
        TestThread.RunSta(() =>
        {
            var panel = PanelWithHeights(10, 20);

            panel.Measure(new Size(900, 1000));

            Assert.Equal(900, panel.DesiredSize.Width, 3);
            Assert.Equal(20, panel.DesiredSize.Height, 3);
        });
    }

    [Fact]
    public void NarrowLayout_StacksChildren()
    {
        TestThread.RunSta(() =>
        {
            var panel = PanelWithHeights(10, 20);

            panel.Measure(new Size(700, 1000));

            // 10 + gap (30) + 20
            Assert.Equal(60, panel.DesiredSize.Height, 3);
        });
    }

    [Fact]
    public void Breakpoint_IsInclusiveAt750()
    {
        TestThread.RunSta(() =>
        {
            var panel = PanelWithHeights(10, 20);

            panel.Measure(new Size(750, 1000));
            Assert.Equal(20, panel.DesiredSize.Height, 3);

            panel.Measure(new Size(749, 1000));
            Assert.Equal(60, panel.DesiredSize.Height, 3);
        });
    }

    private static ResponsiveColumnsPanel PanelWithHeights(double first, double second)
    {
        var panel = new ResponsiveColumnsPanel();
        panel.Children.Add(new Border { Height = first });
        panel.Children.Add(new Border { Height = second });
        return panel;
    }
}
