using System.Windows;
using System.Windows.Controls;

namespace AIConsole.Views;

public partial class TextPromptWindow : Window
{
    private TextPromptWindow(string label, string initialValue)
    {
        InitializeComponent();
        LabelText.Text = label;
        ValueBox.Text = initialValue;
        OkButton.IsEnabled = !string.IsNullOrWhiteSpace(initialValue);
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    /// <summary>Show a single-line prompt and return the trimmed value, or null when cancelled.</summary>
    public static string? Ask(Window? owner, string title, string label, string initialValue)
    {
        var window = new TextPromptWindow(label, initialValue) { Title = title };
        if (owner is not null) window.Owner = owner;
        return window.ShowDialog() == true ? window.ValueBox.Text.Trim() : null;
    }

    private void Value_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OkButton is not null) OkButton.IsEnabled = !string.IsNullOrWhiteSpace(ValueBox.Text);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text)) return;
        DialogResult = true;
    }
}
