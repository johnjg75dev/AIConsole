using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AIConsole.Models;

namespace AIConsole.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>
/// Compares the selected conversation with a sidebar item. Returns a bool or a
/// Visibility, depending on the target property, so the active row can be marked
/// without adding UI state to the Conversation model.
/// </summary>
public sealed class IsActiveConversationConverter : IMultiValueConverter
{
    public static readonly IsActiveConversationConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var active = values.Length == 2 && IsSame(values[0] as Conversation, values[1] as Conversation);
        return targetType == typeof(Visibility)
            ? active ? Visibility.Visible : Visibility.Collapsed
            : active;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool IsSame(Conversation? selected, Conversation? item) =>
        item is not null && selected is not null &&
        (ReferenceEquals(selected, item) || selected.Id == item.Id);
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Compact conversation activity label: time today, "Yesterday", then a date.</summary>
public sealed class ActivityTimeConverter : IValueConverter
{
    public static readonly ActivityTimeConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset timestamp) return "";
        var local = timestamp.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today) return local.ToString("HH:mm", culture);
        if (local.Date == today.AddDays(-1)) return "Yesterday";
        return local.Year == today.Year ? local.ToString("MMM d", culture) : local.ToString("yyyy-MM-dd", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
