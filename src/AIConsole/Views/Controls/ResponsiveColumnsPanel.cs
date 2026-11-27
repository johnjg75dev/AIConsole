using System.Windows;
using System.Windows.Controls;

namespace AIConsole.Views.Controls;

/// <summary>
/// Two-column form panel that collapses to one column when the available width
/// cannot hold both columns comfortably.
/// </summary>
public sealed class ResponsiveColumnsPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(360.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(30.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    private bool UseTwoColumns(double width) =>
        InternalChildren.Count == 2 && !double.IsInfinity(width) && width >= MinColumnWidth * 2 + Gap;

    protected override Size MeasureOverride(Size availableSize)
    {
        var wide = UseTwoColumns(availableSize.Width);
        var columnWidth = wide
            ? (availableSize.Width - Gap) / 2
            : (double.IsInfinity(availableSize.Width) ? MinColumnWidth : availableSize.Width);

        double height = 0;
        var seen = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            height = wide ? Math.Max(height, child.DesiredSize.Height)
                          : height + child.DesiredSize.Height + (seen > 0 ? Gap : 0);
            seen++;
        }

        var widthResult = double.IsInfinity(availableSize.Width) ? columnWidth : availableSize.Width;
        return new Size(widthResult, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var wide = UseTwoColumns(finalSize.Width);
        var columnWidth = wide ? Math.Max(0, (finalSize.Width - Gap) / 2) : finalSize.Width;
        double y = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (wide)
            {
                var index = InternalChildren.IndexOf(child);
                var x = index == 0 ? 0 : columnWidth + Gap;
                child.Arrange(new Rect(x, 0, columnWidth, child.DesiredSize.Height));
            }
            else
            {
                child.Arrange(new Rect(0, y, columnWidth, child.DesiredSize.Height));
                y += child.DesiredSize.Height + Gap;
            }
        }

        return finalSize;
    }
}
