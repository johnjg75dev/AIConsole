using System.Windows;
using System.Windows.Controls;

namespace AIConsole.Converters;

/// <summary>Two-way binding support for PasswordBox.Password.</summary>
public static class PasswordBoxBinding
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static string GetPassword(DependencyObject d) => (string)d.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);

    private const string FlagKey = "PasswordBoxBinding_Updating";

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (box.GetValue(FlagKeyProperty) is true) return;

        SetFlag(box, true);
        try { box.Password = e.NewValue as string ?? ""; }
        finally { SetFlag(box, false); }

        box.PasswordChanged -= BoxOnPasswordChanged;
        box.PasswordChanged += BoxOnPasswordChanged;
    }

    private static void BoxOnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        if (box.GetValue(FlagKeyProperty) is true) return;

        SetFlag(box, true);
        try { SetPassword(box, box.Password); }
        finally { SetFlag(box, false); }
    }

    private static void SetFlag(DependencyObject d, bool value) => d.SetValue(FlagKeyProperty, value);

    private static readonly DependencyProperty FlagKeyProperty = DependencyProperty.RegisterAttached(
        FlagKey, typeof(bool), typeof(PasswordBoxBinding), new PropertyMetadata(false));
}
