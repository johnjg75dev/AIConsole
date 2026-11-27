using System.Windows;
using System.Windows.Controls;
using AIConsole.ViewModels;

namespace AIConsole.Views;

public partial class SettingsView : UserControl
{
    public SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (HotkeyCapture is not null && ViewModel is { } vm)
                HotkeyCapture.ValueChanged += hk => vm.SetHotkey(hk);
        };
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string hex) ViewModel?.SetAccent(hex);
    }

    private void ImportFromFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Config files (*.json;*.jsonc)|*.json;*.jsonc|All files (*.*)|*.*",
            Title = "Import endpoint configuration",
        };
        if (dlg.ShowDialog() == true) ViewModel?.ImportFromFile(dlg.FileName);
    }
}
