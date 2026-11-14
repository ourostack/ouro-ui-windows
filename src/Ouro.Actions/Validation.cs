// Checks a call's input against the action's JSON Schema, after any door's own decoding (an App Action's URI query,
// an MCP call's arguments). It covers the subset core's generator writes: type (with null), enum, const, minimum,
// maximum, maxLength, format, items, properties, required, additionalProperties false and oneOf.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ouro.Actions;

/// <summary>Validates input against an action's input schema.</summary>
public static class OuroValidation
{
    /// <summary>Every way <paramref name="value"/> breaks <paramref name="schema"/>, each with where and how to fix it; empty when it fits.</summary>
    public static IReadOnlyList<string> Problems(JsonObject schema, JsonNode? value)
    {
        var problems = new List<string>();
        Check(schema, value, "input", problems);
        return problems;
    }

    private static void Check(JsonObject schema, JsonNode? value, string at, List<string> problems)
    {
        if (schema["oneOf"] is JsonArray options)
        {
            var fits = options.OfType<JsonObject>().Count(o => { var p = new List<string>(); Check(o, value, at, p); return p.Count == 0; });
            if (fits != 1) problems.Add($"{at}: fits {fits} of its {options.Count} shapes, and must fit exactly one.");
            return;
        }
        var types = schema["type"] switch
        {
            JsonValue v => new[] { v.GetValue<string>() },
            JsonArray a => a.Select(t => t!.GetValue<string>()).ToArray(),
            _ => Array.Empty<string>(),
        };
        var kind = KindOf(value);
        if (types.Length > 0 && !types.Any(t => t == kind || (t == "number" && kind == "integer")))
        {
            problems.Add($"{at}: is {Describe(value)}, and must be {string.Join(" or ", types)}.");
            return;
        }
        if (value is null) return;
        if (schema["const"] is JsonNode c && !JsonNode.DeepEquals(c, value)) problems.Add($"{at}: must be {c.ToJsonString()}.");
        if (schema["enum"] is JsonArray values && !values.Any(v => JsonNode.DeepEquals(v, value)))
            problems.Add($"{at}: is {value.ToJsonString()}, and must be one of {string.Join(", ", values.Select(v => v!.ToJsonString()))}.");
        if (kind is "number" or "integer")
        {
            var n = double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture);
            if (schema["minimum"] is JsonValue min && n < double.Parse(min.ToJsonString(), CultureInfo.InvariantCulture)) problems.Add($"{at}: is {n.ToString(CultureInfo.InvariantCulture)}, below its minimum {min.ToJsonString()}.");
            if (schema["maximum"] is JsonValue max && n > double.Parse(max.ToJsonString(), CultureInfo.InvariantCulture)) problems.Add($"{at}: is {n.ToString(CultureInfo.InvariantCulture)}, above its maximum {max.ToJsonString()}.");
        }
        if (kind == "string")
        {
            var s = value.GetValue<string>();
            if (schema["maxLength"] is JsonValue ml && s.Length > int.Parse(ml.ToJsonString(), CultureInfo.InvariantCulture)) problems.Add($"{at}: is {s.Length} characters, longer than its maximum {ml.ToJsonString()}.");
            if (schema["format"] is JsonValue f && !FitsFormat(f.GetValue<string>(), s)) problems.Add($"{at}: is not a valid {f.GetValue<string>()}.");
        }
        if (kind == "array" && schema["items"] is JsonObject items)
        {
            var i = 0;
            foreach (var item in value.AsArray()) Check(items, item, $"{at}[{i++}]", problems);
        }
        if (kind == "object")
        {
            var obj = value.AsObject();
            var props = schema["properties"] as JsonObject ?? new JsonObject();
            if (schema["required"] is JsonArray required)
                foreach (var r in required.Select(x => x!.GetValue<string>()))
                    if (!obj.ContainsKey(r)) problems.Add($"{at}.{r}: is required and missing.");
            foreach (var (key, v) in obj)
            {
                if (props[key] is JsonObject p) Check(p, v, $"{at}.{key}", problems);
                else if (schema["additionalProperties"] is JsonValue ap && ap.GetValueKind() == JsonValueKind.False)
                    problems.Add($"{at}.{key}: is not an input this action takes. Fix: send only {string.Join(", ", props.Select(x => x.Key))}.");
            }
        }
    }

    private static string KindOf(JsonNode? v) => v switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue j => j.GetValueKind() switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Number => IsInteger(j) ? "integer" : "number",
            JsonValueKind.Null => "null",
            _ => "unknown",
        },
        _ => "unknown",
    };

    private static bool IsInteger(JsonValue j) => double.TryParse(j.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && Math.Floor(d) == d && !double.IsInfinity(d);

    private static string Describe(JsonNode? v) => v is null ? "missing or null" : $"a {KindOf(v)} ({v.ToJsonString()})";

    private static bool FitsFormat(string format, string s) => format switch
    {
        "date-time" => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) && s.Contains('T'),
        "date" => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "uri" => Uri.TryCreate(s, UriKind.Absolute, out _),
        "email" => s.IndexOf('@') is > 0 and var at && at < s.Length - 1 && !s.Contains(' '),
        _ => true,
    };
}
