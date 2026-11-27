using System.Windows;
using System.Windows.Controls;

namespace AIConsole.Converters;

/// <summary>Two-way binding support for PasswordBox.Password.</summary>
public static class PasswordBoxBinding
{
    private static bool _updating;

    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static string GetPassword(DependencyObject d) => (string)d.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (_updating) return;
        _updating = true;
        box.Password = e.NewValue as string ?? "";
        _updating = false;

        box.PasswordChanged -= BoxOnPasswordChanged;
        box.PasswordChanged += BoxOnPasswordChanged;
    }

    private static void BoxOnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        _updating = true;
        SetPassword(box, box.Password);
        _updating = false;
    }
}
