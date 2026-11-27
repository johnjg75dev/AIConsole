using System.Windows;
using System.Windows.Controls;
using AIConsole.Services;
using AIConsole.ViewModels;

namespace AIConsole.Views;

public partial class SettingsView : UserControl
{
    public SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (HotkeyCapture is null || ViewModel is not { } vm) return;
        HotkeyCapture.Value = Hotkey.Parse(vm.HotkeyText);
        HotkeyCapture.ValueChanged += OnHotkeyChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (HotkeyCapture is not null) HotkeyCapture.ValueChanged -= OnHotkeyChanged;
    }

    private void OnHotkeyChanged(Hotkey hk) => ViewModel?.SetHotkey(hk);

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string hex) ViewModel?.SetAccent(hex);
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Binding initialization and Cancel-restore also change SelectedItem; only
        // an interaction with the picker should turn off "match Windows".
        if (sender is not ComboBox combo || (!combo.IsKeyboardFocusWithin && !combo.IsDropDownOpen)) return;
        if (ViewModel is { FollowWindowsTheme: true } vm) vm.FollowWindowsTheme = false;
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
