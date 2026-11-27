using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AIConsole.Services;

public sealed record ThemeInfo(string Name, string File, bool IsLight);

public static class ThemeManager
{
    public const string DefaultTheme = "Midnight Neon";

    public static readonly IReadOnlyList<ThemeInfo> Available = new List<ThemeInfo>
    {
        new("Midnight Neon", "MidnightNeon", false),
        new("Cyber Glass", "CyberGlass", false),
        new("Ember", "Ember", false),
        new("Nord", "Nord", false),
        new("Forest", "Forest", false),
        new("Solar Flare", "SolarFlare", true),
        new("Clean Day", "CleanDay", true),
    };

    /// <summary>Curated accent swatches offered in Settings (override per-theme accent).</summary>
    public static readonly IReadOnlyList<string> AccentSwatches = new List<string>
    {
        "#7C3AED", "#22D3EE", "#F43F5E", "#F59E0B", "#10B981", "#3B82F6", "#EC4899", "#84CC16", "#EF4444", "#F8FAFC",
    };

    public static string Current { get; private set; } = DefaultTheme;
    public static event Action<string>? ThemeChanged;

    public static void Apply(string? name, string? accentHex = null, bool followWindows = false)
    {
        var app = Application.Current;
        if (app is null) return;

        if (followWindows && name is null)
        {
            name = WindowsPrefersLight ? "Clean Day" : DefaultTheme;
        }

        var info = Available.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                   ?? Available.First(t => t.Name == DefaultTheme);

        if (followWindows)
        {
            var light = WindowsPrefersLight;
            info = Available.First(t => t.IsLight == light && (light ? t.Name == "Clean Day" : t.Name == DefaultTheme));
        }

        var tokens = new ResourceDictionary { Source = new Uri("Themes/Tokens.xaml", UriKind.Relative) };
        var palette = new ResourceDictionary { Source = new Uri($"Themes/Palettes/{info.File}.xaml", UriKind.Relative) };

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(tokens);
        app.Resources.MergedDictionaries.Add(palette);

        if (!string.IsNullOrWhiteSpace(accentHex))
            ApplyAccent(palette, accentHex!);

        Current = info.Name;
        ThemeChanged?.Invoke(Current);
    }

    private static void ApplyAccent(ResourceDictionary palette, string accentHex)
    {
        if (!TryColor(accentHex, out var accent)) return;
        var lighter = Lighten(accent, 0.22);
        var dim = WithAlpha(accent, 0x55);

        SetBrush(palette, "AccentBrush", accent);
        SetBrush(palette, "AccentSecondaryBrush", lighter);
        SetBrush(palette, "AccentDimBrush", dim);
        palette["AccentGradientBrush"] = new LinearGradientBrush(accent, lighter, 45);

        var bubble = new LinearGradientBrush(accent, lighter, 45);
        palette["BubbleUserBrush"] = bubble;
        // readable text on accent
        var onAccent = ContrastColor(accent);
        SetBrush(palette, "OnAccentBrush", onAccent);
        SetBrush(palette, "BubbleUserTextBrush", onAccent);
    }

    private static void SetBrush(ResourceDictionary d, string key, Color c)
    {
        var b = new SolidColorBrush(c); b.Freeze(); d[key] = b;
    }

    private static bool TryColor(string hex, out Color color)
    {
        try { color = (Color)ColorConverter.ConvertFromString(hex)!; return true; }
        catch { color = Colors.Transparent; return false; }
    }

    private static Color Lighten(Color c, double amount)
    {
        byte L(byte v) => (byte)Math.Clamp(v + (255 - v) * amount, 0, 255);
        return Color.FromRgb(L(c.R), L(c.G), L(c.B));
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color ContrastColor(Color c)
    {
        var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance > 0.6 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White;
    }

    public static bool WindowsPrefersLight
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return (key?.GetValue("AppsUseLightTheme") as int?) == 1;
            }
            catch { return false; }
        }
    }
}
