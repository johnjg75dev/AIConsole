using System.Windows.Controls;
using System.Windows.Input;
using AIConsole.Services;

namespace AIConsole.Views.Controls;

/// <summary>Click, then press a key combination to set a global hotkey.</summary>
public partial class HotkeyBox : UserControl
{
    private Hotkey _value = Hotkey.Default;

    public event Action<Hotkey>? ValueChanged;

    public HotkeyBox()
    {
        InitializeComponent();
        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;
        GotFocus += (_, _) => CaptureBorder.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        LostFocus += (_, _) => CaptureBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
    }

    public Hotkey Value
    {
        get => _value;
        set { _value = value; Display.Text = value.ToString(); }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var hk = Hotkey.FromKeyEvent(key, Keyboard.Modifiers);
        if (hk.Key == Key.None) { e.Handled = true; return; }
        _value = hk;
        Display.Text = hk.ToString();
        ValueChanged?.Invoke(hk);
        e.Handled = true;
    }
}
