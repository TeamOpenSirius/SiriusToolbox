using Sirius.Tooling.Core.Master.Serialization;
using System.Buffers;
using System.Collections;
using System.Text.Json;
using MessagePack;

namespace Sirius.Tooling.Core.Master.Export;

public sealed class MasterMemoryExporter(string? schemaPath, Action<string>? progress = null)
{
    private readonly Action<string> _progress = progress ?? Console.WriteLine;
    private readonly Dictionary<string, TableSchema> _schemas = schemaPath is not null && File.Exists(schemaPath)
        ? TableSchema.Load(schemaPath)
        : new Dictionary<string, TableSchema>(StringComparer.Ordinal);

    public async Task ExportAsync(string databasePath, string outputDirectory, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(databasePath, ct);
        var sequence = new ReadOnlySequence<byte>(bytes);
        var reader = new MessagePackReader(sequence);
        var headerRaw = reader.ReadRaw();
        var baseOffset = checked((int)reader.Consumed);
        var header = DecodeObject(headerRaw);
        var offsets = AsStringDictionary(header);

        Directory.CreateDirectory(outputDirectory);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var exportedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, rangeObject) in offsets.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                _progress($"Exporting {name}...");
                var range = AsObjectList(rangeObject);
                if (range.Count < 2) throw new InvalidDataException($"Bad offset entry for {name}.");
                var offset = Convert.ToInt32(range[0]);
                var length = Convert.ToInt32(range[1]);
                var start = checked(baseOffset + offset);
                if (start < 0 || length < 0 || start + length > bytes.Length)
                    throw new InvalidDataException($"Range of {name} is outside mastermemory.db.");

                var value = DecodeObject(new ReadOnlySequence<byte>(bytes, start, length));
                var mapped = _schemas.TryGetValue(name, out var schema)
                    ? schema.Map(value, _schemas, name)
                    : Normalize(value);
                var path = Path.Combine(outputDirectory, name + ".json");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(mapped, jsonOptions), ct);
                exportedFiles.Add(Path.GetFullPath(path));
                _progress($"Exported {name}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidDataException($"Failed to export master table '{name}'.", ex);
            }
        }

        foreach (var stalePath in Directory.EnumerateFiles(outputDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (!exportedFiles.Contains(Path.GetFullPath(stalePath)))
            {
                File.Delete(stalePath);
                _progress($"Removed stale master table {Path.GetFileName(stalePath)}");
            }
        }
    }

    internal static object DecodeObject(ReadOnlySequence<byte> raw)
    {
        try { return MessagePackSerializer.Deserialize<object>(raw, MasterMessagePack.StandardOptions); }
        catch { return MessagePackSerializer.Deserialize<object>(raw, MasterMessagePack.Lz4BlockArrayOptions); }
    }

    internal static object? Normalize(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case IDictionary dictionary:
            {
                var result = new Dictionary<string, object?>();
                foreach (DictionaryEntry entry in dictionary)
                    result[Convert.ToString(entry.Key) ?? string.Empty] = Normalize(entry.Value);
                return result;
            }
        }

        if (value is IEnumerable enumerable and not string and not byte[])
        {
            var result = new List<object?>();
            foreach (var item in enumerable) result.Add(Normalize(item));
            return result;
        }
        if (value is byte[] bytes) return Convert.ToBase64String(bytes);
        return value;
    }

    internal static Dictionary<string, object?> AsStringDictionary(object? value)
    {
        if (value is not IDictionary map) throw new InvalidDataException("MasterMemory header is not a map.");
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in map)
            result[Convert.ToString(entry.Key) ?? throw new InvalidDataException("Null master table name.")] = entry.Value;
        return result;
    }

    internal static List<object?> AsObjectList(object? value, string? path = null)
    {
        if (value is not IEnumerable enumerable || value is string || value is byte[])
            throw new InvalidDataException(path is null
                ? $"Expected array, got {value?.GetType().FullName ?? "null"}."
                : $"Expected array at {path}, got {value?.GetType().FullName ?? "null"}.");
        var result = new List<object?>();
        foreach (var item in enumerable) result.Add(item);
        return result;
    }
}

internal sealed class TableSchema
{
    public string Type { get; init; } = string.Empty;
    public JsonElement Value { get; init; }

    public static Dictionary<string, TableSchema> Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, TableSchema>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var type = property.Value.GetProperty("type").GetString() ?? string.Empty;
            result[property.Name] = new TableSchema
            {
                Type = type,
                Value = property.Value.GetProperty("value").Clone()
            };
        }
        return result;
    }

    public object? Map(object? original, IReadOnlyDictionary<string, TableSchema> schemas, string path = "$")
    {
        if (original is null) return null;

        if (Type == "enum")
        {
            var raw = Convert.ToString(original) ?? string.Empty;
            foreach (var item in Value.EnumerateArray())
                if ((item.GetProperty("value").GetString() ?? string.Empty) == raw)
                    return item.GetProperty("name").GetString();
            return original;
        }

        if (Type != "class") return MasterMemoryExporter.Normalize(original);
        var rows = MasterMemoryExporter.AsObjectList(original, path);
        // A table payload is normally an array of rows. A nested class payload is one row.
        if (rows.Count == 0) return rows;
        if (rows[0] is IEnumerable and not string and not byte[])
            return rows.Select((row, index) => MapClassRow(row, schemas, $"{path}[{index}]")).ToList();
        return MapClassRow(original, schemas, path);
    }

    public object? UnmapTable(JsonElement source, IReadOnlyDictionary<string, TableSchema> schemas, string path)
    {
        if (Type != "class")
            throw new InvalidDataException($"Master table schema '{path}' is not a class.");
        if (source.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Expected JSON array at {path}.");

        return source.EnumerateArray()
            .Select((row, index) => UnmapClassRow(row, schemas, $"{path}[{index}]"))
            .ToList();
    }

    public object? OverlayTable(
        JsonElement source,
        object? template,
        IReadOnlyDictionary<string, TableSchema> schemas,
        string path)
    {
        if (Type != "class")
            throw new InvalidDataException($"Master table schema '{path}' is not a class.");
        if (source.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Expected JSON array at {path}.");

        var templateRows = MasterMemoryExporter.AsObjectList(template, path);
        return source.EnumerateArray()
            .Select((row, index) => index < templateRows.Count
                ? OverlayClassRow(row, templateRows[index], schemas, $"{path}[{index}]")
                : UnmapClassRow(row, schemas, $"{path}[{index}]"))
            .ToArray();
    }

    private object? OverlayClassRow(
        JsonElement source,
        object? template,
        IReadOnlyDictionary<string, TableSchema> schemas,
        string path)
    {
        if (source.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Expected JSON object at {path}.");

        var values = MasterMemoryExporter.AsObjectList(template, path).ToArray();
        foreach (var field in Value.EnumerateArray())
        {
            var keyText = field.TryGetProperty("key", out var keyNode) ? keyNode.GetString() : null;
            if (!int.TryParse(keyText, out var key) || key < 0 || key >= values.Length)
                continue;

            var name = field.GetProperty("name").GetString() ?? $"field_{key}";
            if (!source.TryGetProperty(name, out var jsonValue))
                continue;

            var declaredType = field.GetProperty("type").GetString() ?? string.Empty;
            values[key] = OverlayValue(jsonValue, declaredType, values[key], schemas, $"{path}.{name}");
        }

        return values;
    }

    private static object? OverlayValue(
        JsonElement source,
        string declaredType,
        object? template,
        IReadOnlyDictionary<string, TableSchema> schemas,
        string path)
    {
        if (source.ValueKind == JsonValueKind.Null)
            return null;

        var type = declaredType;
        if (type.StartsWith("Nullable<", StringComparison.Ordinal) && type.EndsWith('>'))
            type = type[9..^1];

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            if (source.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Expected JSON array at {path}.");

            var itemType = type[..^2];
            var templateItems = template is null
                ? Array.Empty<object?>()
                : MasterMemoryExporter.AsObjectList(template, path).ToArray();
            if (schemas.TryGetValue(itemType, out var itemSchema))
            {
                return source.EnumerateArray()
                    .Select((item, index) => index < templateItems.Length
                        ? itemSchema.OverlayClassRow(item, templateItems[index], schemas, $"{path}[{index}]")
                        : itemSchema.Unmap(item, schemas, $"{path}[{index}]"))
                    .ToArray();
            }

            var converted = ConvertJsonValue(source, type, schemas, path);
            return CoerceToTemplate(converted, template);
        }

        if (schemas.TryGetValue(type, out var schema))
            return schema.Type == "class"
                ? schema.OverlayClassRow(source, template, schemas, path)
                : CoerceToTemplate(schema.Unmap(source, schemas, path), template);

        return CoerceToTemplate(ConvertJsonValue(source, type, schemas, path), template);
    }

    private static object? CoerceToTemplate(object? value, object? template)
    {
        if (value is null || template is null || template is byte[])
            return value;

        if (template is IEnumerable templateEnumerable and not string &&
            value is IEnumerable valueEnumerable and not string and not byte[])
        {
            var templates = templateEnumerable.Cast<object?>().ToArray();
            var values = valueEnumerable.Cast<object?>().ToArray();
            return values.Select((item, index) => CoerceToTemplate(
                    item,
                    templates.Length == 0 ? null : templates[Math.Min(index, templates.Length - 1)]))
                .ToArray();
        }

        var targetType = template.GetType();
        if (targetType == value.GetType())
            return value;
        if (template is IConvertible && value is IConvertible)
            return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
        return value;
    }

    private object? Unmap(JsonElement source, IReadOnlyDictionary<string, TableSchema> schemas, string path)
    {
        if (source.ValueKind == JsonValueKind.Null)
            return null;

        if (Type == "enum")
        {
            if (source.ValueKind == JsonValueKind.Number && source.TryGetInt32(out var numericValue))
                return numericValue;

            var text = source.ValueKind == JsonValueKind.String ? source.GetString() : source.ToString();
            foreach (var item in Value.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString();
                var value = item.GetProperty("value").GetString();
                if (string.Equals(name, text, StringComparison.Ordinal) ||
                    string.Equals(value, text, StringComparison.Ordinal))
                    return int.Parse(value!, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (int.TryParse(text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out numericValue))
                return numericValue;

            throw new InvalidDataException($"Unknown enum value '{text}' at {path}.");
        }

        return Type == "class"
            ? UnmapClassRow(source, schemas, path)
            : ConvertJsonValue(source, Type, schemas, path);
    }

    private object? UnmapClassRow(
        JsonElement source,
        IReadOnlyDictionary<string, TableSchema> schemas,
        string path)
    {
        if (source.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Expected JSON object at {path}.");

        var fields = Value.EnumerateArray()
            .Select(field => new
            {
                Node = field,
                KeyText = field.TryGetProperty("key", out var keyNode) ? keyNode.GetString() : null
            })
            .Where(field => int.TryParse(field.KeyText, out var key) && key >= 0)
            .Select(field => new
            {
                field.Node,
                Key = int.Parse(field.KeyText!, System.Globalization.CultureInfo.InvariantCulture)
            })
            .ToArray();
        var values = new object?[fields.Length == 0 ? 0 : fields.Max(field => field.Key) + 1];

        foreach (var field in fields)
        {
            var name = field.Node.GetProperty("name").GetString() ?? $"field_{field.Key}";
            if (!source.TryGetProperty(name, out var value))
                throw new InvalidDataException($"Missing field '{name}' at {path}.");
            var type = field.Node.GetProperty("type").GetString() ?? string.Empty;
            values[field.Key] = ConvertJsonValue(value, type, schemas, $"{path}.{name}");
        }

        return values;
    }

    private static object? ConvertJsonValue(
        JsonElement source,
        string declaredType,
        IReadOnlyDictionary<string, TableSchema> schemas,
        string path)
    {
        if (source.ValueKind == JsonValueKind.Null)
            return null;

        var type = declaredType;
        if (type.StartsWith("Nullable<", StringComparison.Ordinal) && type.EndsWith('>'))
            type = type[9..^1];

        if (type == "byte[]")
            return Convert.FromBase64String(source.GetString() ?? string.Empty);

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            if (source.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Expected JSON array at {path}.");
            var itemType = type[..^2];
            return source.EnumerateArray()
                .Select((item, index) => ConvertJsonValue(item, itemType, schemas, $"{path}[{index}]"))
                .ToList();
        }

        if (schemas.TryGetValue(type, out var schema))
            return schema.Unmap(source, schemas, path);

        return type switch
        {
            "bool" => source.GetBoolean(),
            "byte" => source.GetByte(),
            "sbyte" => source.GetSByte(),
            "short" => source.GetInt16(),
            "ushort" => source.GetUInt16(),
            "int" => source.GetInt32(),
            "uint" => source.GetUInt32(),
            "long" => source.GetInt64(),
            "ulong" => source.GetUInt64(),
            "float" => source.GetSingle(),
            "double" => source.GetDouble(),
            "Decimal" or "decimal" => source.GetDecimal(),
            "string" => source.GetString(),
            "DateTime" => source.GetDateTime(),
            "Guid" => source.GetGuid(),
            _ => ConvertUntyped(source, path)
        };
    }

    private static object? ConvertUntyped(JsonElement source, string path) => source.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => source.GetString(),
        JsonValueKind.Number when source.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => source.GetDouble(),
        JsonValueKind.Array => source.EnumerateArray()
            .Select((item, index) => ConvertUntyped(item, $"{path}[{index}]"))
            .ToList(),
        JsonValueKind.Object => source.EnumerateObject()
            .ToDictionary(property => property.Name, property => ConvertUntyped(property.Value, $"{path}.{property.Name}")),
        _ => throw new InvalidDataException($"Unsupported JSON value at {path}.")
    };

    private object? MapClassRow(object? original, IReadOnlyDictionary<string, TableSchema> schemas, string path)
    {
        if (original is null) return null;

        var values = MasterMemoryExporter.AsObjectList(original, path);
        var result = new Dictionary<string, object?>();
        foreach (var field in Value.EnumerateArray())
        {
            var keyText = field.TryGetProperty("key", out var keyNode) ? keyNode.GetString() : null;
            if (!int.TryParse(keyText, out var key) || key < 0 || key >= values.Count) continue;
            var name = field.GetProperty("name").GetString() ?? $"field_{key}";
            var type = field.GetProperty("type").GetString() ?? string.Empty;
            var value = values[key];
            var fieldPath = $"{path}.{name}";
            if (schemas.TryGetValue(type.TrimEnd('[', ']'), out var child))
            {
                if (type.EndsWith("[]", StringComparison.Ordinal))
                    value = value is null
                        ? null
                        : MasterMemoryExporter.AsObjectList(value, fieldPath)
                            .Select((x, index) => child.Map(x, schemas, $"{fieldPath}[{index}]")).ToList();
                else
                    value = child.Map(value, schemas, fieldPath);
            }
            else value = MasterMemoryExporter.Normalize(value);
            result[name] = value;
        }
        return result;
    }
}
