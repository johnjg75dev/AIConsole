using System.IO;
using System.Text.Json;

namespace AIConsole.Services;

/// <summary>An endpoint discovered in another tool's config.</summary>
public sealed record ImportedEndpoint(string Name, string BaseUrl, string? ApiKey, IReadOnlyList<string> Models, string Source);

/// <summary>
/// Scans known installations of other AI tools and imports OpenAI-compatible endpoints.
/// Currently first-class: OpenCode (+auth.json). Also parses Continue and Zed configs.
/// </summary>
public sealed class ConfigImporter
{
    private readonly OpenAiClient _client;

    public ConfigImporter(OpenAiClient client) => _client = client;

    // ── known locations ──

    private static IEnumerable<string> OpenCodeConfigPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var dirs = new[] { Path.Combine(home, ".config", "opencode"), appdata + "\\opencode", local + "\\opencode", (xdg ?? "") + "\\opencode", Directory.GetCurrentDirectory() };
        foreach (var d in dirs.Where(d => !string.IsNullOrEmpty(d)))
        {
            yield return Path.Combine(d, "opencode.json");
            yield return Path.Combine(d, "opencode.jsonc");
        }
    }

    private static IEnumerable<string> OpenCodeAuthPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(home, ".local", "share", "opencode", "auth.json");
        yield return Path.Combine(home, ".config", "opencode", "auth.json");
        yield return Path.Combine(appdata, "opencode", "auth.json");
        yield return Path.Combine(local, "opencode", "auth.json");
    }

    public List<ImportedEndpoint> ScanKnownLocations()
    {
        var found = new List<ImportedEndpoint>();

        // ── OpenCode ──
        var authKeys = ReadOpenCodeAuthKeys();
        foreach (var path in OpenCodeConfigPaths())
        {
            var json = TryRead(path);
            if (json is null) continue;
            foreach (var ep in ParseOpenCode(json, authKeys, source: "OpenCode (" + Path.GetFileName(path) + ")"))
                found.Add(ep);
        }

        // ── Continue ──
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var continuePath = Path.Combine(home, ".continue", "config.json");
        var cont = TryRead(continuePath);
        if (cont is not null) found.AddRange(ParseContinue(cont, "Continue"));

        // ── Zed ──
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var zedPath in new[]
                 {
                     Path.Combine(appdata, "Zed", "settings.json"),
                     Path.Combine(home, ".config", "zed", "settings.json"),
                 })
        {
            var z = TryRead(zedPath);
            if (z is not null) found.AddRange(ParseZed(z, "Zed"));
        }

        return found
            .GroupBy(e => (e.Name, e.BaseUrl, e.ApiKey))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Import from an arbitrary config file; auto-detects OpenCode / Continue / Zed / plain shapes.</summary>
    public List<ImportedEndpoint> ParseConfigFile(string path)
    {
        var json = TryRead(path);
        if (json is null) return new();
        return ParseConfig(json, Path.GetFileName(path));
    }

    public List<ImportedEndpoint> ParseConfig(string json, string source)
    {
        var authKeys = ReadOpenCodeAuthKeys();
        var list = new List<ImportedEndpoint>();
        list.AddRange(ParseOpenCode(json, authKeys, source));
        list.AddRange(ParseContinue(json, source));
        list.AddRange(ParseZed(json, source));
        list.AddRange(ParsePlain(json, source));
        return list.GroupBy(e => (e.Name, e.BaseUrl, e.ApiKey)).Select(g => g.First()).ToList();
    }

    /// <summary>Probe well-known local OpenAI-compatible servers.</summary>
    public async Task<List<ImportedEndpoint>> DetectLocalServersAsync(CancellationToken ct = default)
    {
        var candidates = new (string Name, string Url)[]
        {
            ("Ollama", "http://localhost:11434/v1"),
            ("LM Studio", "http://localhost:1234/v1"),
            ("Local server", "http://localhost:8080/v1"),
            ("Local server", "http://localhost:5000/v1"),
            ("Local server", "http://localhost:8000/v1"),
        };
        var found = new List<ImportedEndpoint>();
        var checks = candidates.Select(async c =>
        {
            try
            {
                var models = await _client.ListModelsAsync(c.Url, null, ct).ConfigureAwait(false);
                return new ImportedEndpoint(c.Name, c.Url, null, models, "local");
            }
            catch { return null; }
        });
        var results = await Task.WhenAll(checks).ConfigureAwait(false);
        found.AddRange(results.Where(r => r is not null).Select(r => r!));
        return found.GroupBy(e => e.BaseUrl).Select(g => g.First()).ToList();
    }

    // ── parsers ──

    public List<ImportedEndpoint> ParseOpenCode(string json, Dictionary<string, string> authKeys, string source)
    {
        var found = new List<ImportedEndpoint>();
        try
        {
            using var doc = JsonDocument.Parse(StripJsonComments(json));
            var root = doc.RootElement;
            if (!root.TryGetProperty("provider", out var providers) || providers.ValueKind != JsonValueKind.Object)
                return found;

            foreach (var prov in providers.EnumerateObject())
            {
                var p = prov.Value;
                var npm = p.TryGetProperty("npm", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                string? baseUrl = null; string? apiKey = null;
                if (p.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Object)
                {
                    if (opts.TryGetProperty("baseURL", out var b) && b.ValueKind == JsonValueKind.String) baseUrl = b.GetString();
                    else if (opts.TryGetProperty("baseUrl", out var b2) && b2.ValueKind == JsonValueKind.String) baseUrl = b2.GetString();
                    if (opts.TryGetProperty("apiKey", out var k) && k.ValueKind == JsonValueKind.String) apiKey = k.GetString();
                }

                // only OpenAI-compatible providers (or any with explicit baseURL)
                bool compatible = npm.Contains("openai-compatible", StringComparison.OrdinalIgnoreCase)
                                  || npm.Contains("openai", StringComparison.OrdinalIgnoreCase)
                                  || !string.IsNullOrWhiteSpace(baseUrl);
                if (!compatible || string.IsNullOrWhiteSpace(baseUrl)) continue;

                apiKey = ResolveSecret(apiKey);
                if (string.IsNullOrWhiteSpace(apiKey) && authKeys.TryGetValue(prov.Name, out var authKey)) apiKey = authKey;

                var models = new List<string>();
                if (p.TryGetProperty("models", out var modelsEl))
                {
                    if (modelsEl.ValueKind == JsonValueKind.Object)
                        models.AddRange(modelsEl.EnumerateObject().Select(m => m.Name));
                    else if (modelsEl.ValueKind == JsonValueKind.Array)
                        foreach (var m in modelsEl.EnumerateArray())
                            if (m.ValueKind == JsonValueKind.String) models.Add(m.GetString()!);
                            else if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) models.Add(id.GetString()!);
                            else if (m.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) models.Add(nm.GetString()!);
                }

                var name = p.TryGetProperty("name", out var nm2) && nm2.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nm2.GetString())
                    ? nm2.GetString()! : prov.Name;
                found.Add(new ImportedEndpoint(name, baseUrl!.TrimEnd('/'), apiKey, models, source));
            }
        }
        catch { }
        return found;
    }

    private Dictionary<string, string> ReadOpenCodeAuthKeys()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in OpenCodeAuthPaths())
        {
            var json = TryRead(path);
            if (json is null) continue;
            try
            {
                using var doc = JsonDocument.Parse(StripJsonComments(json));
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    var v = entry.Value;
                    string? key = null;
                    if (v.ValueKind == JsonValueKind.Object)
                    {
                        if (v.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String) key = k.GetString();
                        else if (v.TryGetProperty("apiKey", out var k2) && k2.ValueKind == JsonValueKind.String) key = k2.GetString();
                    }
                    else if (v.ValueKind == JsonValueKind.String) key = v.GetString();
                    key = ResolveSecret(key);
                    if (!string.IsNullOrEmpty(key)) map[entry.Name] = key!;
                }
            }
            catch { }
        }
        return map;
    }

    public List<ImportedEndpoint> ParseContinue(string json, string source)
    {
        var found = new List<ImportedEndpoint>();
        try
        {
            using var doc = JsonDocument.Parse(StripJsonComments(json));
            var root = doc.RootElement;
            if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return found;

            foreach (var m in models.EnumerateArray())
            {
                var provider = m.TryGetProperty("provider", out var pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString() ?? "" : "";
                var providerLower = provider.ToLowerInvariant();
                string? baseUrl = m.TryGetProperty("apiBase", out var ab) && ab.ValueKind == JsonValueKind.String ? ab.GetString() : null;
                baseUrl ??= m.TryGetProperty("api_base", out var ab2) && ab2.ValueKind == JsonValueKind.String ? ab2.GetString() : null;

                string? defaultBase = providerLower switch
                {
                    "ollama" => "http://localhost:11434/v1",
                    "lmstudio" or "lm-studio" => "http://localhost:1234/v1",
                    "openai" => "https://api.openai.com/v1",
                    _ => null,
                };
                baseUrl ??= defaultBase;

                // only OpenAI-compatible providers
                bool compatible = providerLower is "openai" or "openai-compatible" or "openai_compatible" or "ollama" or "lmstudio" or "lm-studio"
                                  || !string.IsNullOrWhiteSpace(baseUrl);
                if (!compatible || string.IsNullOrWhiteSpace(baseUrl)) continue;

                var model = m.TryGetProperty("model", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() : null;
                var title = m.TryGetProperty("title", out var tv) && tv.ValueKind == JsonValueKind.String ? tv.GetString() : null;
                var apiKey = m.TryGetProperty("apiKey", out var kv) && kv.ValueKind == JsonValueKind.String ? kv.GetString() : null;
                apiKey = ResolveSecret(apiKey);

                var modelList = new List<string>();
                if (!string.IsNullOrWhiteSpace(model)) modelList.Add(model!);

                found.Add(new ImportedEndpoint(title ?? model ?? provider, baseUrl!.TrimEnd('/'), apiKey, modelList, source));
            }
        }
        catch { }
        return found;
    }

    public List<ImportedEndpoint> ParseZed(string json, string source)
    {
        var found = new List<ImportedEndpoint>();
        try
        {
            using var doc = JsonDocument.Parse(StripJsonComments(json));
            var root = doc.RootElement;
            if (!root.TryGetProperty("language_models", out var lm)) return found;
            if (!lm.TryGetProperty("openai_compatible", out var oc) || oc.ValueKind != JsonValueKind.Object) return found;

            foreach (var prov in oc.EnumerateObject())
            {
                var p = prov.Value;
                var apiUrl = p.TryGetProperty("api_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(apiUrl)) continue;

                var models = new List<string>();
                if (p.TryGetProperty("available_models", out var am) && am.ValueKind == JsonValueKind.Array)
                    foreach (var m in am.EnumerateArray())
                    {
                        if (m.ValueKind == JsonValueKind.String) models.Add(m.GetString()!);
                        else if (m.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) models.Add(nm.GetString()!);
                    }

                string? apiKey = null;
                if (p.TryGetProperty("api_key", out var k) && k.ValueKind == JsonValueKind.String) apiKey = k.GetString();

                found.Add(new ImportedEndpoint(prov.Name, apiUrl!.TrimEnd('/'), apiKey, models, source));
            }
        }
        catch { }
        return found;
    }

    private List<ImportedEndpoint> ParsePlain(string json, string source)
    {
        var found = new List<ImportedEndpoint>();
        try
        {
            using var doc = JsonDocument.Parse(StripJsonComments(json));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return found;

            string? baseUrl = FirstString(root, "baseURL", "baseUrl", "base_url", "apiBase", "api_base", "api_url", "url", "endpoint");
            if (string.IsNullOrWhiteSpace(baseUrl)) return found;
            string? apiKey = FirstString(root, "apiKey", "api_key", "key", "token");
            var name = FirstString(root, "name", "title", "provider") ?? "Imported";

            var models = new List<string>();
            if (root.TryGetProperty("models", out var m))
            {
                if (m.ValueKind == JsonValueKind.Array)
                    foreach (var x in m.EnumerateArray())
                        if (x.ValueKind == JsonValueKind.String) models.Add(x.GetString()!);
                else if (m.ValueKind == JsonValueKind.Object)
                    models.AddRange(m.EnumerateObject().Select(o => o.Name));
            }
            var model = FirstString(root, "model");
            if (!string.IsNullOrEmpty(model)) models.Add(model);

            found.Add(new ImportedEndpoint(name, baseUrl!.TrimEnd('/'), ResolveSecret(apiKey), models, source));
        }
        catch { }
        return found;
    }

    // ── helpers ──

    private static string? FirstString(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
            if (obj.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString();
        return null;
    }

    private static string? ResolveSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        // OpenCode "{env:VAR}" / "env:VAR" references
        string? varName = null;
        if (v.StartsWith("{env:", StringComparison.OrdinalIgnoreCase) && v.EndsWith('}')) varName = v[5..^1];
        else if (v.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) varName = v[4..];
        if (varName is not null) return Environment.GetEnvironmentVariable(varName);
        return v;
    }

    private static string? TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch { return null; }
    }

    /// <summary>Strip // and /* *&#47; comments for .jsonc.</summary>
    public static string StripJsonComments(string json)
    {
        var sb = new System.Text.StringBuilder(json.Length);
        bool inStr = false, inLine = false, inBlock = false; char strCh = '"';
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            char next = i + 1 < json.Length ? json[i + 1] : '\0';
            if (inLine)
            {
                if (c == '\n') { inLine = false; sb.Append(c); }
                continue;
            }
            if (inBlock)
            {
                if (c == '*' && next == '/') { inBlock = false; i++; }
                continue;
            }
            if (inStr)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                else if (c == strCh) inStr = false;
                continue;
            }
            if (c == '"' || c == '\'') { inStr = true; strCh = c; sb.Append(c); continue; }
            if (c == '/' && next == '/') { inLine = true; i++; continue; }
            if (c == '/' && next == '*') { inBlock = true; i++; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
