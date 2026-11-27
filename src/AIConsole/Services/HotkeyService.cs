using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AIConsole.Services;

/// <summary>One global hotkey registered with Win32 RegisterHotKey.</summary>
public sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, Key Key)
{
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;

    public uint Modifiers =>
        (Ctrl ? MOD_CONTROL : 0) | (Alt ? MOD_ALT : 0) | (Shift ? MOD_SHIFT : 0) | (Win ? MOD_WIN : 0) | MOD_NOREPEAT;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public bool HasModifier => Ctrl || Alt || Shift || Win;

    public static readonly Hotkey Default = new(Ctrl: true, Alt: false, Shift: false, Win: false, Key: Key.OemTilde);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyToString(Key));
        return string.Join("+", parts);
    }

    public static string KeyToString(Key k) => k switch
    {
        Key.OemTilde => "`",
        Key.Space => "Space",
        Key.Escape => "Esc",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        _ => k.ToString(),
    };

    public static Hotkey Parse(string s)
    {
        try
        {
            var ctrl = false; var alt = false; var shift = false; var win = false; Key key = Key.OemTilde;
            foreach (var raw in s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var p = raw.Trim();
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": ctrl = true; break;
                    case "alt": alt = true; break;
                    case "shift": shift = true; break;
                    case "win": case "windows": case "meta": win = true; break;
                    default:
                        key = p.ToLowerInvariant() switch
                        {
                            "`" => Key.OemTilde,
                            "space" => Key.Space,
                            "esc" or "escape" => Key.Escape,
                            "enter" or "return" => Key.Return,
                            "+" => Key.OemPlus,
                            "-" => Key.OemMinus,
                            "," => Key.OemComma,
                            "." => Key.OemPeriod,
                            "/" => Key.OemQuestion,
                            ";" => Key.OemSemicolon,
                            "'" => Key.OemQuotes,
                            "[" => Key.OemOpenBrackets,
                            "]" => Key.OemCloseBrackets,
                            "\\" => Key.OemPipe,
                            _ => Enum.TryParse<Key>(p, true, out var k) ? k : key,
                        };
                        break;
                }
            }
            return new Hotkey(ctrl, alt, shift, win, key);
        }
        catch { return Default; }
    }

    public static Hotkey FromKeyEvent(Key key, ModifierKeys mods)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            key = Key.None;
        return new Hotkey(
            mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt),
            mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Windows), key);
    }
}

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xB00C;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private Hotkey? _current;
    private readonly Action _onPressed;

    public event Action<string>? RegistrationFailed;

    public HotkeyService(IntPtr hwnd, HwndSource source, Action onPressed)
    {
        _hwnd = hwnd; _source = source; _onPressed = onPressed;
        _source.AddHook(WndProc);
    }

    public bool Register(Hotkey hotkey)
    {
        UnregisterHotKey(_hwnd, HotkeyId);
        _current = null;
        var ok = RegisterHotKey(_hwnd, HotkeyId, hotkey.Modifiers, hotkey.VirtualKey);
        if (ok) _current = hotkey;
        else RegistrationFailed?.Invoke($"Couldn't register {hotkey}. Another app may already use it.");
        return ok;
    }

    public Hotkey? Current => _current;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            _onPressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try { UnregisterHotKey(_hwnd, HotkeyId); _source.RemoveHook(WndProc); } catch { }
    }
}
