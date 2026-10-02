using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sirius.Toolbox.Master.Operations;

public static class MasterOperationJson
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static IReadOnlyDictionary<string, object?> RequireMap(object? record, string table, string key)
    {
        if (record is IReadOnlyDictionary<string, object?> readOnly) return readOnly;
        if (record is IDictionary<string, object?> mutable)
            return new Dictionary<string, object?>(mutable, StringComparer.OrdinalIgnoreCase);
        throw new InvalidDataException($"{table}[{key}] is not an object record.");
    }

    public static string? FindName(IReadOnlyDictionary<string, object?> values, params string[] candidates)
    {
        var key = FindProperty(values, candidates);
        return key is null ? null : Convert.ToString(values[key], CultureInfo.InvariantCulture);
    }

    public static string? FindProperty(IReadOnlyDictionary<string, object?> values, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var exact = values.Keys.FirstOrDefault(x => string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }
        return null;
    }

    public static string? FindPropertyContaining(IReadOnlyDictionary<string, object?> values, params string[] tokens)
    {
        return values.Keys.FirstOrDefault(name => tokens.All(token =>
            name.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    public static long? GetInt64(IReadOnlyDictionary<string, object?> values, params string[] candidates)
    {
        var name = FindProperty(values, candidates);
        return name is null ? null : ToInt64(values[name]);
    }

    public static long? ToInt64(object? value)
    {
        if (value is null) return null;
        try
        {
            return value switch
            {
                long v => v,
                int v => v,
                short v => v,
                byte v => v,
                uint v => v,
                ulong v when v <= long.MaxValue => (long)v,
                JsonElement { ValueKind: JsonValueKind.Number } e when e.TryGetInt64(out var v) => v,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }

    public static DateTimeOffset? GetDate(IReadOnlyDictionary<string, object?> values, params string[] candidates)
    {
        var name = FindProperty(values, candidates);
        return name is null ? null : ToDate(values[name]);
    }

    public static DateTimeOffset? ToDate(object? value)
    {
        if (value is null) return null;
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt),
            JsonElement { ValueKind: JsonValueKind.String } element when element.TryGetDateTimeOffset(out var dto) => dto,
            string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto) => dto,
            _ => null
        };
    }

    public static string DisplayName(IReadOnlyDictionary<string, object?> values, string fallback)
    {
        foreach (var candidate in new[]
                 {
                     "Name", "DisplayName", "Title", "EventName", "GachaName", "ShopName", "MusicName",
                     "Description", "Label"
                 })
        {
            var value = FindName(values, candidate);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return fallback;
    }

    public static Dictionary<string, object?> CreateEndPatch(
        IReadOnlyDictionary<string, object?> values,
        DateTimeOffset target,
        bool includeForceEnd = true)
    {
        var patch = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (!IsEndProperty(pair.Key, includeForceEnd)) continue;
            if (ToDate(pair.Value) is null) continue;
            patch[pair.Key] = target;
        }
        return patch;
    }

    public static bool IsEndProperty(string name, bool includeForceEnd = true)
    {
        if (!name.Contains("End", StringComparison.OrdinalIgnoreCase)) return false;
        if (!includeForceEnd && name.Contains("Force", StringComparison.OrdinalIgnoreCase)) return false;
        return name.Contains("Date", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Time", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith("End", StringComparison.OrdinalIgnoreCase);
    }

    public static JsonObject ToJsonObject(IReadOnlyDictionary<string, object?> values)
    {
        return JsonSerializer.SerializeToNode(values, JsonOptions) as JsonObject
               ?? throw new InvalidDataException("Failed to convert MasterData record to JSON object.");
    }

    public static int ReplaceNestedEndDates(JsonNode? node, DateTimeOffset target)
    {
        if (node is null) return 0;
        var changed = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj.ToArray())
                {
                    if (IsEndProperty(property.Key) && TryReadJsonDate(property.Value, out _))
                    {
                        obj[property.Key] = target;
                        changed++;
                    }
                    else
                    {
                        changed += ReplaceNestedEndDates(property.Value, target);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    changed += ReplaceNestedEndDates(item, target);
                break;
        }
        return changed;
    }

    public static IEnumerable<(string Path, JsonObject Value)> EnumerateNestedObjects(JsonNode? node, string path = "$")
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                var childPath = path + "." + property.Key;
                if (property.Value is JsonArray array)
                {
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (array[i] is JsonObject item)
                            yield return (childPath + $"[{i}]", item);
                    }
                }
            }
        }
    }

    public static IReadOnlyDictionary<string, object?> JsonObjectToMap(JsonObject value)
    {
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(value.ToJsonString(), JsonOptions)
               ?? new Dictionary<string, object?>();
    }

    private static bool TryReadJsonDate(JsonNode? node, out DateTimeOffset value)
    {
        value = default;
        if (node is not JsonValue scalar || !scalar.TryGetValue<string>(out var text)) return false;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    public static object? NormalizeJsonValue(object? value)
    {
        if (value is not JsonElement element) return value;
        return element.ValueKind switch
        {
            JsonValueKind.String when element.TryGetDateTimeOffset(out var dto) => dto,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var i) => i,
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone()
        };
    }
}
