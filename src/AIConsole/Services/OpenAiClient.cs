using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIConsole.Services;

public sealed record ChatMessageDto(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

/// <summary>Minimal client for any OpenAI-compatible server (/models + /chat/completions, SSE streaming).</summary>
public sealed class OpenAiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, string? apiKey, CancellationToken ct = default)
    {
        var tried = new List<string>();
        foreach (var url in ModelListCandidates(baseUrl))
        {
            tried.Add(url);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyAuth(req, apiKey);
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseModelList(json);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* try next candidate */ }
        }
        throw new InvalidOperationException("Could not retrieve models from endpoint. Tried: " + string.Join(", ", tried));
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string baseUrl, string? apiKey, string model, IReadOnlyList<ChatMessageDto> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = CombineUrl(baseUrl, "chat/completions");
        var payload = new
        {
            model,
            messages,
            stream = true,
            temperature = 0.7,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(req, apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http
            .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {Truncate(body, 400)}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var sawSseData = false;
        var raw = new StringBuilder();

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (line.Length == 0) continue;

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                sawSseData = true;
                var data = line[5..].Trim();
                if (data == "[DONE]") yield break;
                var delta = ExtractDelta(data);
                if (delta is not null) yield return delta;
            }
            else if (line.StartsWith(':'))
            {
                // SSE comment / keep-alive
            }
            else if (!sawSseData)
            {
                raw.AppendLine(line); // non-SSE response body accumulating
            }
        }

        // Some servers ignore stream=true and return a plain JSON completion.
        if (!sawSseData && raw.Length > 0)
        {
            var text = ExtractFullMessage(raw.ToString());
            if (text is not null) yield return text;
        }
    }

    private static string? ExtractDelta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return null;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                return c.GetString();
            if (choice.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var mc) && mc.ValueKind == JsonValueKind.String)
                return mc.GetString();
            // some providers stream "text"
            if (choice.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
            return null;
        }
        catch { return null; }
    }

    private static string? ExtractFullMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return null;
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var mc) && mc.ValueKind == JsonValueKind.String)
                return mc.GetString();
            if (choice.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
            return null;
        }
        catch { return null; }
    }

    private static List<string> ParseModelList(string json)
    {
        var ids = new List<string>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // {"data":[{"id":"..."}], ...}  (OpenAI style)
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in data.EnumerateArray())
            {
                if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    ids.Add(id.GetString()!);
                else if (m.ValueKind == JsonValueKind.String)
                    ids.Add(m.GetString()!);
            }
        }
        // {"models":[{"name":"..."}|{"id":..}|"..",...]} (some servers)
        else if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in models.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.String) { ids.Add(m.GetString()!); continue; }
                if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) { ids.Add(id.GetString()!); continue; }
                if (m.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) ids.Add(name.GetString()!);
            }
        }
        // bare array ["a","b"] or [{"id":"a"}]
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in root.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.String) { ids.Add(m.GetString()!); continue; }
                if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) ids.Add(id.GetString()!);
            }
        }

        return ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string CombineUrl(string baseUrl, string path)
    {
        var b = (baseUrl ?? "").Trim().TrimEnd('/');
        // don't double-append /v1
        if (b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return b + "/" + path;
        return b + "/v1/" + path;
    }

    private static IEnumerable<string> ModelListCandidates(string baseUrl)
    {
        var b = (baseUrl ?? "").Trim().TrimEnd('/');
        if (b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            yield return b + "/models";
            yield return b + "/../models"; // tolerate odd roots
        }
        else
        {
            yield return b + "/v1/models";
            yield return b + "/models";
        }
    }

    private static void ApplyAuth(HttpRequestMessage req, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    public void Dispose() => _http.Dispose();
}
