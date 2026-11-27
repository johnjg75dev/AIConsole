using System.Globalization;
using System.Text.Json;

namespace AIConsole.Models;

/// <summary>A presentation of a System One response. The original response is always retained.</summary>
public sealed record DecisionProbability(string Label, string DisplayValue, double BarValue, bool HasBar);
public sealed record DecisionAnswer(string Name, string Type, string Value, string Confidence,
    IReadOnlyList<DecisionProbability> Probabilities);
public sealed record DecisionResult(string Model, IReadOnlyList<DecisionAnswer> Answers, string Raw, string Notice)
{
    public static DecisionResult Parse(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var raw = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
            var root = json.RootElement;
            // Providers may wrap the System One result in data.result or result.
            var result = root;
            if (GetObject(result, "data") is { } data) result = data;
            if (GetObject(result, "result") is { } nested) result = nested;
            var model = GetText(result, "model") ?? GetText(root, "model") ?? "";
            var answers = new List<DecisionAnswer>();
            if (GetObject(result, "answers") is { } answerObject)
            {
                foreach (var property in answerObject.EnumerateObject())
                {
                    var answer = property.Value;
                    if (answer.ValueKind != JsonValueKind.Object) continue;
                    var type = GetText(answer, "type") ?? "unknown";
                    var value = type.ToLowerInvariant() switch
                    {
                        "choice" => GetText(answer, "choice"),
                        "score" => GetText(answer, "score"),
                        "noul" => GetProbabilityText(answer, "noul"),
                        _ => null,
                    };
                    var probabilities = new List<DecisionProbability>();
                    if (GetObject(answer, "probabilities") is { } probabilityObject)
                    {
                        foreach (var probability in probabilityObject.EnumerateObject())
                        {
                            var valid = probability.Value.ValueKind == JsonValueKind.Number &&
                                probability.Value.TryGetDouble(out var number) && number >= 0 && number <= 1;
                            var bar = valid ? probability.Value.GetDouble() * 100 : 0;
                            probabilities.Add(new DecisionProbability(probability.Name,
                                valid ? FormatPercent(bar) : ElementText(probability.Value), bar, valid));
                        }
                    }
                    answers.Add(new DecisionAnswer(property.Name, type, value ?? "Not provided",
                        GetProbabilityText(answer, "confidence") ?? "", probabilities));
                }
            }
            var notice = answers.Count == 0
                ? "No recognizable answers were found. See the raw response below."
                : "";
            return new DecisionResult(model, answers, raw, notice);
        }
        catch (JsonException)
        {
            return new DecisionResult("", Array.Empty<DecisionAnswer>(), body,
                "The response was not valid JSON. See the raw response below.");
        }
    }

    private static JsonElement? GetObject(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : null;

    private static string? GetText(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
            ? ElementText(value) : null;

    private static string? GetProbabilityText(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number >= 0 && number <= 1
            ? FormatPercent(number * 100) : ElementText(value);
    }

    private static string ElementText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
    private static string FormatPercent(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
