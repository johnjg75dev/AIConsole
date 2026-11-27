using System.Text.Json;

namespace AIConsole.Data;

public sealed class SettingsRepo
{
    private readonly Db _db;
    public SettingsRepo(Db db) => _db = db;

    public string? GetString(string key, string? fallback = null)
    {
        var v = _db.Scalar("SELECT value FROM settings WHERE key=@k", ("@k", key));
        return v as string ?? fallback;
    }

    public void SetString(string key, string? value)
    {
        _db.Run("INSERT INTO settings(key,value) VALUES(@k,@v) ON CONFLICT(key) DO UPDATE SET value=@v",
            ("@k", key), ("@v", value));
    }

    public T Get<T>(string key, T fallback)
    {
        var raw = GetString(key);
        if (raw is null) return fallback;
        try { return JsonSerializer.Deserialize<T>(raw) ?? fallback; }
        catch { return fallback; }
    }

    public void Set<T>(string key, T value) => SetString(key, JsonSerializer.Serialize(value));
}

public static class SettingKeys
{
    public const string Settings = "settings";
    public const string Theme = "theme";
    public const string Accent = "accent";
    public const string Hotkey = "hotkey";
    public const string SlideEdge = "slideEdge";
    public const string StartWithWindows = "startWithWindows";
}
