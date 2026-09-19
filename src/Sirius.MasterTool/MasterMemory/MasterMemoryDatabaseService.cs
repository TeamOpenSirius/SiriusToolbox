using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MasterMemory.Meta;
using MessagePack;
using Sirius.MasterData;
using Sirius.Protocol.Shared;
using Sirius.Toolbox.IO;

namespace Sirius.MasterTool.MasterMemory;

public sealed record MasterTableSummary(string Name, string DataType, int RowCount, IReadOnlyList<string> PrimaryKey);
public sealed record MasterSchemaProperty(int Key, string Name, string Type, bool Writable, bool PrimaryKey);
public sealed record MasterRecordView(int Row, string PrimaryKey, object? Record);
public sealed record MasterDatabaseVerification(int TableCount, long RowCount, string Sha256);
public sealed record MasterDatabaseWriteResult(string OutputPath, string Table, int RowCount, string Sha256);

public static class MasterMemoryDatabaseService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Regex JapaneseRegex = new(@"[\u3040-\u30ff\u3400-\u9fff]", RegexOptions.Compiled);
    private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = new();

    public static MasterDatabaseVerification Verify(string databasePath)
    {
        var bytes = File.ReadAllBytes(databasePath);
        var db = Open(bytes);
        ValidateGeneratedConstraints(db);
        var meta = MemoryDatabase.GetMetaDatabase();
        long rows = 0;

        foreach (var info in meta.GetTableInfos())
        {
            var table = GetTable(db, info);
            rows += GetRawRows(table).Length;
        }

        return new MasterDatabaseVerification(meta.Count, rows, Sha256(bytes));
    }

    public static IReadOnlyList<MasterTableSummary> GetTables(string databasePath, string? contains = null)
    {
        var db = Open(File.ReadAllBytes(databasePath));
        var meta = MemoryDatabase.GetMetaDatabase();
        var result = new List<MasterTableSummary>();

        foreach (var info in meta.GetTableInfos().OrderBy(x => x.TableName, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(contains)
                && !info.TableName.Contains(contains, StringComparison.OrdinalIgnoreCase))
                continue;

            var rows = GetRawRows(GetTable(db, info));
            result.Add(new MasterTableSummary(
                info.TableName,
                info.DataType.FullName ?? info.DataType.Name,
                rows.Length,
                PrimaryKeyNames(info)));
        }

        return result;
    }

    public static IReadOnlyList<MasterSchemaProperty> GetSchema(string databasePath, string tableName)
    {
        _ = Open(File.ReadAllBytes(databasePath));
        var table = GetTableInfo(tableName);
        var primary = PrimaryKeyNames(table).ToHashSet(StringComparer.Ordinal);
        return MessagePackProperties(table.DataType)
            .Select((property, position) =>
            {
                var key = NumericKey(property);
                return new MasterSchemaProperty(
                    key >= 0 ? key : position,
                    property.Name,
                    FriendlyTypeName(property.PropertyType),
                    property.CanWrite,
                    primary.Contains(property.Name));
            })
            .ToArray();
    }

    public static IReadOnlyList<MasterRecordView> ListRecords(
        string databasePath,
        string tableName,
        int offset = 0,
        int limit = 50)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));

        var db = Open(File.ReadAllBytes(databasePath));
        var info = GetTableInfo(tableName);
        var rows = GetRawRows(GetTable(db, info));
        var primary = GetPrimaryIndex(info);
        var end = Math.Min(rows.Length, checked(offset + limit));
        var result = new List<MasterRecordView>(Math.Max(0, end - offset));

        for (var i = offset; i < end; i++)
        {
            var row = rows.GetValue(i);
            if (row is null) continue;
            result.Add(new MasterRecordView(i, FormatPrimaryKey(row, primary), ToJsonValue(row, info.DataType)));
        }

        return result;
    }

    public static MasterRecordView GetRecord(string databasePath, string tableName, string key)
    {
        var db = Open(File.ReadAllBytes(databasePath));
        var info = GetTableInfo(tableName);
        var rows = GetRawRows(GetTable(db, info));
        var primary = RequirePrimaryIndex(info);
        var index = FindRowIndex(rows, primary, info.DataType, key);
        if (index < 0) throw new KeyNotFoundException($"Record not found: {tableName} key={key}");
        var row = rows.GetValue(index)
            ?? throw new InvalidDataException($"Null row: {tableName}[{index}]");
        return new MasterRecordView(index, FormatPrimaryKey(row, primary), ToJsonValue(row, info.DataType));
    }

    public static MasterDatabaseWriteResult AddRecord(
        string databasePath,
        string tableName,
        string recordJson,
        string outputPath)
    {
        var original = File.ReadAllBytes(databasePath);
        var db = Open(original);
        var info = GetTableInfo(tableName);
        var table = GetTable(db, info);
        var rows = GetRawRows(table);
        var primary = RequirePrimaryIndex(info);

        using var json = JsonDocument.Parse(recordJson);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("New record JSON must be an object.");

        var record = CreateInstance(info.DataType);
        ApplyJsonObject(record, info.DataType, json.RootElement, patch: false, path: tableName);
        var newKey = FormatPrimaryKey(record, primary);
        if (FindRowIndex(rows, primary, info.DataType, newKey) >= 0)
            throw new InvalidDataException($"Duplicate primary key in {tableName}: {newKey}");

        var expanded = Array.CreateInstance(info.DataType, rows.Length + 1);
        Array.Copy(rows, expanded, rows.Length);
        expanded.SetValue(record, rows.Length);
        return RebuildChangedTable(original, info, expanded, outputPath);
    }

    public static MasterDatabaseWriteResult UpdateRecord(
        string databasePath,
        string tableName,
        string key,
        string patchJson,
        string outputPath)
    {
        var original = File.ReadAllBytes(databasePath);
        var db = Open(original);
        var info = GetTableInfo(tableName);
        var table = GetTable(db, info);
        var rows = GetRawRows(table);
        var primary = RequirePrimaryIndex(info);
        var index = FindRowIndex(rows, primary, info.DataType, key);
        if (index < 0) throw new KeyNotFoundException($"Record not found: {tableName} key={key}");

        var row = rows.GetValue(index)
            ?? throw new InvalidDataException($"Null row: {tableName}[{index}]");
        var beforeKey = FormatPrimaryKey(row, primary);

        using var json = JsonDocument.Parse(patchJson);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Update JSON must be an object.");
        ApplyJsonObject(row, info.DataType, json.RootElement, patch: true, path: tableName);
        rows.SetValue(row, index);

        var afterKey = FormatPrimaryKey(row, primary);
        if (!string.Equals(beforeKey, afterKey, StringComparison.Ordinal)
            && FindDuplicateKey(rows, primary, info.DataType, afterKey, index))
            throw new InvalidDataException($"Update would create duplicate primary key in {tableName}: {afterKey}");

        return RebuildChangedTable(original, info, rows, outputPath);
    }

    public static MasterDatabaseWriteResult DeleteRecord(
        string databasePath,
        string tableName,
        string key,
        string outputPath)
    {
        var original = File.ReadAllBytes(databasePath);
        var db = Open(original);
        var info = GetTableInfo(tableName);
        var table = GetTable(db, info);
        var rows = GetRawRows(table);
        var primary = RequirePrimaryIndex(info);
        var index = FindRowIndex(rows, primary, info.DataType, key);
        if (index < 0) throw new KeyNotFoundException($"Record not found: {tableName} key={key}");

        var reduced = Array.CreateInstance(info.DataType, rows.Length - 1);
        if (index > 0) Array.Copy(rows, 0, reduced, 0, index);
        if (index < rows.Length - 1) Array.Copy(rows, index + 1, reduced, index, rows.Length - index - 1);
        return RebuildChangedTable(original, info, reduced, outputPath);
    }

    public static async Task ExportAllJsonAsync(
        string databasePath,
        string outputDirectory,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var db = Open(await File.ReadAllBytesAsync(databasePath, cancellationToken));
        var meta = MemoryDatabase.GetMetaDatabase();
        Directory.CreateDirectory(outputDirectory);
        var exported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var info in meta.GetTableInfos().OrderBy(x => x.TableName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke($"Exporting {info.TableName}...");
            var rows = GetRawRows(GetTable(db, info));
            var records = new object?[rows.Length];
            for (var i = 0; i < rows.Length; i++)
                records[i] = ToJsonValue(rows.GetValue(i), info.DataType);

            var path = Path.Combine(outputDirectory, info.TableName + ".json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records, JsonOptions), cancellationToken);
            exported.Add(Path.GetFullPath(path));
        }

        foreach (var stale in Directory.EnumerateFiles(outputDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (!exported.Contains(Path.GetFullPath(stale))) File.Delete(stale);
        }
    }

    public static int ExportText(string databasePath, string csvPath, bool includeEmpty, bool japaneseOnly)
    {
        var bytes = File.ReadAllBytes(databasePath);
        var db = Open(bytes);
        var meta = MemoryDatabase.GetMetaDatabase();
        var records = new List<TextRecord>();

        foreach (var tableInfo in meta.GetTableInfos())
        {
            var table = GetTable(db, tableInfo);
            var rows = GetRawRows(table);
            var primary = GetPrimaryIndex(tableInfo);
            var primaryNames = primary?.IndexProperties.Select(x => x.Name).ToHashSet(StringComparer.Ordinal)
                               ?? new HashSet<string>(StringComparer.Ordinal);

            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = rows.GetValue(rowIndex);
                if (row is null) continue;
                var primaryKey = FormatPrimaryKey(row, primary);

                WalkStrings(row, tableInfo.DataType, "", (path, value) =>
                {
                    if (!includeEmpty && string.IsNullOrEmpty(value)) return;
                    if (japaneseOnly && !JapaneseRegex.IsMatch(value)) return;
                    var kind = ClassifyString(path, value, primaryNames);
                    var id = StableId(tableInfo.TableName, primaryKey, path, value);
                    records.Add(new TextRecord(id, tableInfo.TableName, rowIndex, primaryKey, path, kind, value, ""));
                });
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath))!);
        using var sw = new StreamWriter(csvPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Csv.WriteRow(sw, "id", "table", "row", "primary_key", "path", "kind", "source", "translation");
        foreach (var r in records)
        {
            Csv.WriteRow(sw, r.Id, r.Table, r.Row.ToString(CultureInfo.InvariantCulture), r.PrimaryKey,
                r.Path, r.Kind, r.Source, r.Translation);
        }
        return records.Count;
    }

    public static MasterDatabaseWriteResult BuildText(string databasePath, string csvPath, string outputPath)
    {
        var originalBytes = File.ReadAllBytes(databasePath);
        var db = Open(originalBytes);
        var meta = MemoryDatabase.GetMetaDatabase();
        var originalLayout = MasterMemoryBinary.Parse(originalBytes);
        var csv = Csv.ReadAll(csvPath).ToList();
        if (csv.Count == 0) throw new InvalidDataException("Translation CSV is empty.");

        var columns = csv[0].Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        int Col(string name) => columns.TryGetValue(name, out var i)
            ? i
            : throw new InvalidDataException($"Missing CSV column: {name}");

        var tableCol = Col("table");
        var rowCol = Col("row");
        var primaryCol = Col("primary_key");
        var pathCol = Col("path");
        _ = Col("kind");
        var sourceCol = Col("source");
        var translationCol = Col("translation");

        var translations = csv.Skip(1)
            .Where(r => r.Length > translationCol && !string.IsNullOrEmpty(r[translationCol]))
            .ToArray();

        if (translations.Length == 0)
        {
            AtomicFile.WriteAllBytes(outputPath, originalBytes);
            return new MasterDatabaseWriteResult(Path.GetFullPath(outputPath), "(none)", 0, Sha256(originalBytes));
        }

        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var changedRows = 0;

        foreach (var group in translations.GroupBy(r => r[tableCol], StringComparer.Ordinal))
        {
            var tableInfo = GetTableInfo(group.Key);
            var typedRows = GetRawRows(GetTable(db, tableInfo));
            var primary = GetPrimaryIndex(tableInfo);
            var primaryNames = primary?.IndexProperties.Select(x => x.Name).ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);

            foreach (var r in group)
            {
                var rowIndex = int.Parse(r[rowCol], CultureInfo.InvariantCulture);
                if ((uint)rowIndex >= (uint)typedRows.Length)
                    throw new InvalidDataException($"Row out of range: {group.Key}[{rowIndex}]");

                var row = typedRows.GetValue(rowIndex)
                    ?? throw new InvalidDataException($"Null row: {group.Key}[{rowIndex}]");
                var actualPrimary = FormatPrimaryKey(row, primary);
                if (!string.Equals(actualPrimary, r[primaryCol], StringComparison.Ordinal))
                    throw new InvalidDataException($"Primary-key mismatch at {group.Key}[{rowIndex}]. CSV is stale.");
                if (IsPrimaryKeyPath(r[pathCol], primaryNames))
                    throw new InvalidDataException($"Refusing to translate a primary-key string: {group.Key} {r[pathCol]}");

                var actualSource = GetStringAtPath(row, r[pathCol]);
                if (!string.Equals(actualSource, r[sourceCol], StringComparison.Ordinal))
                    throw new InvalidDataException($"Source mismatch at {group.Key}[{rowIndex}] {r[pathCol]}. CSV is stale.");
                if (string.Equals(actualSource, r[translationCol], StringComparison.Ordinal)) continue;
                SetStringAtPath(row, r[pathCol], r[translationCol]);
                typedRows.SetValue(row, rowIndex);
                changedRows++;
            }

            replacements[tableInfo.TableName] = BuildSingleTableSegment(tableInfo, typedRows);
        }

        var rebuilt = MasterMemoryBinary.Rebuild(originalBytes, originalLayout, replacements);
        ValidateFullDatabase(rebuilt);
        AtomicFile.WriteAllBytes(outputPath, rebuilt);
        return new MasterDatabaseWriteResult(Path.GetFullPath(outputPath), $"{replacements.Count} table(s)", changedRows, Sha256(rebuilt));
    }

    public static string MergeJsonWithSetValues(string? json, IReadOnlyList<string> setValues, bool requireObject = true)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(json))
        {
            using var document = JsonDocument.Parse(json);
            if (requireObject && document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("JSON payload must be an object.");
            foreach (var property in document.RootElement.EnumerateObject())
                values[property.Name] = property.Value.Clone();
        }

        foreach (var assignment in setValues)
        {
            var equals = assignment.IndexOf('=');
            if (equals <= 0) throw new ArgumentException($"Invalid --set value '{assignment}'. Expected Property=Value.");
            var name = assignment[..equals].Trim();
            var raw = assignment[(equals + 1)..].Trim();
            using var document = ParseCommandLineValue(raw);
            values[name] = document.RootElement.Clone();
        }

        return JsonSerializer.Serialize(values, JsonOptions);
    }

    public static string ReadJsonArgument(string? jsonFile, string? data)
    {
        if (!string.IsNullOrWhiteSpace(jsonFile) && !string.IsNullOrWhiteSpace(data))
            throw new ArgumentException("Use either --json <file> or --data <json>, not both.");
        if (!string.IsNullOrWhiteSpace(jsonFile)) return File.ReadAllText(jsonFile);
        return data ?? "{}";
    }

    private static JsonDocument ParseCommandLineValue(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return JsonDocument.Parse("\"\"");
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(raw));
        }
    }

    private static MasterDatabaseWriteResult RebuildChangedTable(
        byte[] original,
        MetaTable tableInfo,
        Array typedRows,
        string outputPath)
    {
        var originalLayout = MasterMemoryBinary.Parse(original);
        var segment = BuildSingleTableSegment(tableInfo, typedRows);
        var rebuilt = MasterMemoryBinary.Rebuild(original, originalLayout,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [tableInfo.TableName] = segment });
        ValidateFullDatabase(rebuilt);
        AtomicFile.WriteAllBytes(outputPath, rebuilt);
        return new MasterDatabaseWriteResult(Path.GetFullPath(outputPath), tableInfo.TableName, typedRows.Length, Sha256(rebuilt));
    }

    private static byte[] BuildSingleTableSegment(MetaTable tableInfo, Array typedRows)
    {
        var builder = new DatabaseBuilder();
        AppendTyped(builder, tableInfo.DataType, typedRows);
        var oneTableDb = builder.Build();
        var layout = MasterMemoryBinary.Parse(oneTableDb);
        return layout.GetSegment(oneTableDb, tableInfo.TableName).ToArray();
    }

    private static void ValidateFullDatabase(byte[] bytes)
    {
        var db = Open(bytes);
        foreach (var info in MemoryDatabase.GetMetaDatabase().GetTableInfos())
            _ = GetRawRows(GetTable(db, info));
        ValidateGeneratedConstraints(db);
    }

    private static void ValidateGeneratedConstraints(MemoryDatabase database)
    {
        var validate = database.GetType().GetMethod(
            "Validate",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (validate is null) return;

        var result = validate.Invoke(database, null);
        if (result is null) return;
        var failed = result.GetType().GetProperty("IsValidationFailed", BindingFlags.Instance | BindingFlags.Public);
        if (failed?.GetValue(result) is not true) return;

        var format = result.GetType().GetMethod(
            "FormatFailedResults",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        var detail = format?.Invoke(result, null)?.ToString();
        throw new InvalidDataException(string.IsNullOrWhiteSpace(detail)
            ? "MasterMemory generated validation failed."
            : "MasterMemory generated validation failed:" + Environment.NewLine + detail);
    }

    private static MemoryDatabase Open(byte[] bytes) =>
        new(bytes, maxDegreeOfParallelism: Environment.ProcessorCount);

    private static MetaTable GetTableInfo(string tableName) =>
        MemoryDatabase.GetMetaDatabase().GetTableInfo(tableName)
        ?? throw new KeyNotFoundException($"Unknown MasterMemory table: {tableName}");

    private static object GetTable(MemoryDatabase database, MetaTable info) =>
        MemoryDatabase.GetTable(database, info.TableName)
        ?? throw new InvalidOperationException($"Generated table missing: {info.TableName}");

    private static MetaIndex? GetPrimaryIndex(MetaTable info) =>
        info.Indexes.FirstOrDefault(x => x.IsPrimaryIndex);

    private static MetaIndex RequirePrimaryIndex(MetaTable info) =>
        GetPrimaryIndex(info) ?? throw new InvalidOperationException($"Table has no primary index: {info.TableName}");

    private static IReadOnlyList<string> PrimaryKeyNames(MetaTable info) =>
        GetPrimaryIndex(info)?.IndexProperties.Select(x => x.Name).ToArray() ?? [];

    private static Array GetRawRows(object table)
    {
        var method = table.GetType().GetMethod("GetRawDataUnsafe", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(table.GetType().FullName, "GetRawDataUnsafe");
        return (Array)(method.Invoke(table, null)
            ?? throw new InvalidOperationException("GetRawDataUnsafe returned null."));
    }

    private static void AppendTyped(DatabaseBuilder builder, Type dataType, Array rows)
    {
        var append = typeof(DatabaseBuilder).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == "Append" && m.GetParameters().Length == 1)
            .FirstOrDefault(m =>
            {
                var parameter = m.GetParameters()[0].ParameterType;
                return parameter.IsGenericType
                    && parameter.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    && parameter.GetGenericArguments()[0] == dataType;
            });
        if (append is null)
            throw new MissingMethodException($"DatabaseBuilder.Append(IEnumerable<{dataType.Name}>)");
        append.Invoke(builder, new object?[] { rows });
    }

    private static int FindRowIndex(Array rows, MetaIndex primary, Type dataType, string keyText)
    {
        var expected = ParsePrimaryKey(primary, dataType, keyText);
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows.GetValue(i);
            if (row is null) continue;
            if (PrimaryKeyMatches(row, primary, expected)) return i;
        }
        return -1;
    }

    private static bool FindDuplicateKey(Array rows, MetaIndex primary, Type dataType, string keyText, int exceptIndex)
    {
        var expected = ParsePrimaryKey(primary, dataType, keyText);
        for (var i = 0; i < rows.Length; i++)
        {
            if (i == exceptIndex) continue;
            var row = rows.GetValue(i);
            if (row is not null && PrimaryKeyMatches(row, primary, expected)) return true;
        }
        return false;
    }

    private static IReadOnlyDictionary<string, object?> ParsePrimaryKey(MetaIndex primary, Type dataType, string keyText)
    {
        if (primary.IndexProperties.Count() == 1 && !keyText.Contains('='))
        {
            var property = primary.IndexProperties.First();
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [property.Name] = ConvertString(keyText, GetPropertyType(dataType, property.Name))
            };
        }

        var parsed = ParseKeyAssignments(keyText);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in primary.IndexProperties)
        {
            if (!parsed.TryGetValue(property.Name, out var raw))
                throw new ArgumentException($"Composite key must specify '{property.Name}'. Format: {FormatKeyUsage(primary)}");
            result[property.Name] = ConvertString(raw, GetPropertyType(dataType, property.Name));
        }
        return result;
    }

    private static Dictionary<string, string> ParseKeyAssignments(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var name = new StringBuilder();
        var value = new StringBuilder();
        var readingValue = false;
        var escaping = false;

        void Commit()
        {
            if (name.Length == 0) throw new ArgumentException($"Invalid key expression: {text}");
            result[name.ToString()] = value.ToString();
            name.Clear();
            value.Clear();
            readingValue = false;
        }

        foreach (var c in text)
        {
            if (escaping)
            {
                (readingValue ? value : name).Append(c);
                escaping = false;
            }
            else if (c == '\\')
            {
                escaping = true;
            }
            else if (c == '=' && !readingValue)
            {
                readingValue = true;
            }
            else if (c == ';' && readingValue)
            {
                Commit();
            }
            else
            {
                (readingValue ? value : name).Append(c);
            }
        }
        if (escaping) (readingValue ? value : name).Append('\\');
        Commit();
        return result;
    }

    private static bool PrimaryKeyMatches(object row, MetaIndex primary, IReadOnlyDictionary<string, object?> expected)
    {
        foreach (var property in primary.IndexProperties)
        {
            var actual = property.GetValue(row);
            if (!ValueEquals(actual, expected[property.Name])) return false;
        }
        return true;
    }

    private static bool ValueEquals(object? left, object? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.GetType().IsEnum && right.GetType() != left.GetType())
            right = Enum.ToObject(left.GetType(), right);
        return Equals(left, right);
    }

    private static string FormatPrimaryKey(object row, MetaIndex? primary)
    {
        if (primary is null || !primary.IndexProperties.Any()) return "";
        return string.Join(";", primary.IndexProperties.Select(p =>
        {
            var text = FormatScalar(p.GetValue(row));
            return p.Name + "=" + text.Replace("\\", "\\\\").Replace(";", "\\;");
        }));
    }

    private static string FormatKeyUsage(MetaIndex primary) =>
        string.Join(";", primary.IndexProperties.Select(x => x.Name + "=<value>"));

    private static string FormatScalar(object? value) => value switch
    {
        null => "null",
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    private static Type GetPropertyType(Type dataType, string propertyName) =>
        dataType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.PropertyType
        ?? throw new InvalidOperationException($"Property '{propertyName}' not found on {dataType.FullName}.");

    private static object? ConvertString(string text, Type targetType)
    {
        var nullable = Nullable.GetUnderlyingType(targetType);
        var type = nullable ?? targetType;
        if (nullable is not null && string.Equals(text, "null", StringComparison.OrdinalIgnoreCase)) return null;
        if (type == typeof(string)) return text;
        if (type.IsEnum)
        {
            if (Enum.TryParse(type, text, ignoreCase: true, out var named)) return named;
            var underlying = Enum.GetUnderlyingType(type);
            return Enum.ToObject(type, Convert.ChangeType(text, underlying, CultureInfo.InvariantCulture)!);
        }
        if (type == typeof(Guid)) return Guid.Parse(text);
        if (type == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(TimeSpan)) return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return bool.Parse(text);
        return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }

    private static object CreateInstance(Type type)
    {
        if (type.IsValueType) return Activator.CreateInstance(type)!;
        try
        {
            return Activator.CreateInstance(type, nonPublic: true)
                ?? RuntimeHelpers.GetUninitializedObject(type);
        }
        catch (MissingMethodException)
        {
            return RuntimeHelpers.GetUninitializedObject(type);
        }
    }

    private static void ApplyJsonObject(object target, Type targetType, JsonElement source, bool patch, string path)
    {
        var properties = MessagePackProperties(targetType)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var jsonProperty in source.EnumerateObject())
        {
            if (!properties.TryGetValue(jsonProperty.Name, out var property))
                throw new InvalidDataException($"Unknown property '{jsonProperty.Name}' at {path}.");
            if (!property.CanWrite)
                throw new InvalidDataException($"Property is read-only: {path}.{property.Name}");

            var existing = property.CanRead ? property.GetValue(target) : null;
            var value = ConvertJson(jsonProperty.Value, property.PropertyType, existing, $"{path}.{property.Name}");
            property.SetValue(target, value);
            supplied.Add(property.Name);
        }

        if (!patch)
        {
            var primaryNames = MemoryDatabase.GetMetaDatabase().GetTableInfos()
                .FirstOrDefault(x => x.DataType == targetType)?.Indexes
                .FirstOrDefault(x => x.IsPrimaryIndex)?.IndexProperties
                .Select(x => x.Name).ToArray() ?? [];
            foreach (var name in primaryNames)
                if (!supplied.Contains(name))
                    throw new InvalidDataException($"New record is missing primary-key property '{name}'.");
        }
    }

    private static object? ConvertJson(JsonElement source, Type declaredType, object? existing, string path)
    {
        var nullable = Nullable.GetUnderlyingType(declaredType);
        var type = nullable ?? declaredType;
        if (source.ValueKind == JsonValueKind.Null)
        {
            if (!type.IsValueType || nullable is not null) return null;
            throw new InvalidDataException($"Null is not valid for {path} ({FriendlyTypeName(declaredType)}).");
        }

        if (type == typeof(string)) return source.ValueKind == JsonValueKind.String ? source.GetString() : source.ToString();
        if (type == typeof(byte[])) return Convert.FromBase64String(source.GetString() ?? "");
        if (type == typeof(Guid)) return Guid.Parse(source.GetString() ?? "");
        if (type == typeof(DateTime)) return source.GetDateTime();
        if (type == typeof(TimeSpan)) return TimeSpan.Parse(source.GetString() ?? "", CultureInfo.InvariantCulture);

        if (type.IsEnum)
        {
            if (source.ValueKind == JsonValueKind.String)
            {
                var text = source.GetString() ?? "";
                if (Enum.TryParse(type, text, ignoreCase: true, out var named)) return named;
                throw new InvalidDataException($"Unknown enum value '{text}' at {path}.");
            }
            var numeric = source.GetInt64();
            return Enum.ToObject(type, numeric);
        }

        if (type.IsArray)
        {
            if (source.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Expected array at {path}.");
            var elementType = type.GetElementType()!;
            var sourceItems = source.EnumerateArray().ToArray();
            var result = Array.CreateInstance(elementType, sourceItems.Length);
            var existingArray = existing as Array;
            for (var i = 0; i < sourceItems.Length; i++)
            {
                var old = existingArray is not null && i < existingArray.Length ? existingArray.GetValue(i) : null;
                result.SetValue(ConvertJson(sourceItems[i], elementType, old, $"{path}[{i}]"), i);
            }
            return result;
        }

        if (IsScalar(type))
            return JsonSerializer.Deserialize(source.GetRawText(), type);

        if (source.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Expected object at {path} for {FriendlyTypeName(type)}.");
        var target = existing ?? CreateInstance(type);
        ApplyJsonObject(target, type, source, patch: existing is not null, path);
        return target;
    }

    private static object? ToJsonValue(object? value, Type declaredType)
    {
        if (value is null) return null;
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type == typeof(byte[])) return Convert.ToBase64String((byte[])value);
        if (type.IsEnum) return value.ToString();
        if (type == typeof(DateTime)) return ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);
        if (type == typeof(Guid) || type == typeof(TimeSpan) || type == typeof(string) || IsScalar(type)) return value;

        if (type.IsArray)
        {
            var array = (Array)value;
            var elementType = type.GetElementType()!;
            var result = new object?[array.Length];
            for (var i = 0; i < array.Length; i++) result[i] = ToJsonValue(array.GetValue(i), elementType);
            return result;
        }

        if (value is IEnumerable enumerable and not string)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
                list.Add(item is null ? null : ToJsonValue(item, item.GetType()));
            return list;
        }

        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in MessagePackProperties(type))
            map[property.Name] = ToJsonValue(property.GetValue(value), property.PropertyType);
        return map;
    }

    private static PropertyInfo[] MessagePackProperties(Type type)
    {
        lock (PropertyCache)
        {
            if (PropertyCache.TryGetValue(type, out var cached)) return cached;

            var publicReadable = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && !HasIgnoreMember(property))
                .ToArray();
            var keyed = publicReadable
                .Select(property => (Property: property, Key: NumericKey(property)))
                .Where(x => x.Key >= 0)
                .OrderBy(x => x.Key)
                .Select(x => x.Property)
                .ToArray();

            // Sirius.Protocol can contain either integer-key MessagePack objects or
            // [MessagePackObject(true)] objects. If numeric keys are present, use only
            // those serialized members in key order; otherwise MessagePack serializes
            // public properties by name, excluding [IgnoreMember].
            var properties = keyed.Length != 0
                ? keyed
                : publicReadable.OrderBy(property => property.MetadataToken).ToArray();
            PropertyCache[type] = properties;
            return properties;
        }
    }

    private static bool HasIgnoreMember(PropertyInfo property) =>
        property.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == typeof(IgnoreMemberAttribute).FullName);

    private static int NumericKey(PropertyInfo property)
    {
        var key = property.CustomAttributes
            .FirstOrDefault(attribute => attribute.AttributeType.FullName == typeof(KeyAttribute).FullName);
        if (key is null || key.ConstructorArguments.Count == 0) return -1;
        return key.ConstructorArguments[0].Value is int number ? number : -1;
    }

    private static string FriendlyTypeName(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return FriendlyTypeName(nullable) + "?";
        if (type.IsArray) return FriendlyTypeName(type.GetElementType()!) + "[]";
        return type.FullName ?? type.Name;
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(Guid)
        || type == typeof(DateTime) || type == typeof(TimeSpan);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WalkStrings(object? value, Type declaredType, string path, Action<string, string> emit)
    {
        if (value is null) return;
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type == typeof(string))
        {
            emit(path, (string)value);
            return;
        }
        if (type == typeof(byte[]) || IsScalar(type)) return;
        if (type.IsArray)
        {
            var array = (Array)value;
            var elementType = type.GetElementType()!;
            for (var i = 0; i < array.Length; i++)
                WalkStrings(array.GetValue(i), elementType, path + "/" + i.ToString(CultureInfo.InvariantCulture), emit);
            return;
        }
        foreach (var property in MessagePackProperties(type))
            WalkStrings(property.GetValue(value), property.PropertyType, path + "/" + EscapePointer(property.Name), emit);
    }

    private static string GetStringAtPath(object root, string path)
    {
        var (container, last) = ResolveContainer(root, path);
        if (container is Array array)
        {
            var index = ParseArrayIndex(last, array.Length, path);
            return array.GetValue(index) as string
                ?? throw new InvalidDataException($"Path is not a string array element: {path}");
        }
        var property = container.GetType().GetProperty(last, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidDataException($"Property not found: {path}");
        return property.GetValue(container) as string
            ?? throw new InvalidDataException($"Path is not a string property: {path}");
    }

    private static void SetStringAtPath(object root, string path, string value)
    {
        var (container, last) = ResolveContainer(root, path);
        if (container is Array array)
        {
            var index = ParseArrayIndex(last, array.Length, path);
            if (array.GetType().GetElementType() != typeof(string))
                throw new InvalidDataException($"Path is not a string array element: {path}");
            array.SetValue(value, index);
            return;
        }
        var property = container.GetType().GetProperty(last, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidDataException($"Property not found: {path}");
        if (property.PropertyType != typeof(string) || !property.CanWrite)
            throw new InvalidDataException($"Path is not a writable string property: {path}");
        property.SetValue(container, value);
    }

    private static (object Container, string Last) ResolveContainer(object root, string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/') throw new InvalidDataException($"Invalid path: {path}");
        var parts = path.Split('/').Skip(1).Select(UnescapePointer).ToArray();
        if (parts.Length == 0) throw new InvalidDataException($"Invalid path: {path}");
        object current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (current is Array array)
            {
                current = array.GetValue(ParseArrayIndex(part, array.Length, path))
                    ?? throw new InvalidDataException($"Null array element in path: {path}");
            }
            else
            {
                var property = current.GetType().GetProperty(part, BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new InvalidDataException($"Property not found in path: {path}");
                current = property.GetValue(current)
                    ?? throw new InvalidDataException($"Null object in path: {path}");
            }
        }
        return (current, parts[^1]);
    }

    private static int ParseArrayIndex(string text, int length, string path)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || (uint)index >= (uint)length)
            throw new InvalidDataException($"Array index out of range in path: {path}");
        return index;
    }

    private static bool IsPrimaryKeyPath(string path, HashSet<string> primaryNames)
    {
        if (path.Count(c => c == '/') != 1) return false;
        var last = UnescapePointer(path.Split('/').LastOrDefault() ?? "");
        return primaryNames.Contains(last);
    }

    private static string ClassifyString(string path, string value, HashSet<string> primaryNames)
    {
        var last = UnescapePointer(path.Split('/').LastOrDefault() ?? "");
        if (IsPrimaryKeyPath(path, primaryNames)) return "key";
        if (Uri.TryCreate(value, UriKind.Absolute, out _)) return "machine";
        if (Regex.IsMatch(last, "(Path|Url|Uri|File|Asset|Hash|Token|Key|Id)$", RegexOptions.IgnoreCase)) return "machine";
        if (Regex.IsMatch(value, @"^[A-Za-z0-9_./:#?=&%+\-]+$") && !value.Contains(' ')) return "machine";
        return "text";
    }

    private static string StableId(string table, string primaryKey, string path, string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{table}\n{primaryKey}\n{path}\n{source}"));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string EscapePointer(string value) => value.Replace("~", "~0").Replace("/", "~1");
    private static string UnescapePointer(string value) => value.Replace("~1", "/").Replace("~0", "~");

    private sealed record TextRecord(
        string Id,
        string Table,
        int Row,
        string PrimaryKey,
        string Path,
        string Kind,
        string Source,
        string Translation);
}
