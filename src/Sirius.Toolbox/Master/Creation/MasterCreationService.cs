using System.Globalization;
using System.Text.Json;
using Sirius.MasterData;

namespace Sirius.Toolbox.Master.Creation;

public sealed class MasterCreationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public long AllocateNextId(string databasePath, string tableName, long minimum = 1)
    {
        var max = minimum - 1;
        foreach (var row in MasterMemoryDatabaseService.ListRecords(databasePath, tableName, 0, int.MaxValue))
        {
            if (long.TryParse(row.PrimaryKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                max = Math.Max(max, id);
        }
        return checked(max + 1);
    }

    public MasterCreationPreview CreatePreview(string databasePath, MasterCreationDraft draft)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var knownTables = MasterMemoryDatabaseService.GetTables(databasePath)
            .ToDictionary(x => x.Name, StringComparer.Ordinal);
        var batchKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in draft.Records)
        {
            if (!knownTables.ContainsKey(record.TableName))
            {
                errors.Add($"不存在 Master 表：{record.TableName}");
                continue;
            }

            var schema = MasterMemoryDatabaseService.GetSchema(databasePath, record.TableName);
            var writable = schema.Where(x => x.Writable).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var primary = schema.Where(x => x.PrimaryKey).Select(x => x.Name).ToArray();
            foreach (var field in record.Fields.Keys)
            {
                if (!writable.Contains(field)) errors.Add($"{record.TableName} 不允许写入字段：{field}");
            }

            var missingKeys = primary.Where(x => !record.Fields.ContainsKey(x)).ToArray();
            if (missingKeys.Length > 0)
                errors.Add($"{record.TableName} 缺少主键字段：{string.Join(", ", missingKeys)}");

            var key = string.Join("|", primary.Select(x => Convert.ToString(record.Fields.GetValueOrDefault(x), CultureInfo.InvariantCulture) ?? ""));
            if (!batchKeys.Add(record.TableName + "|" + key)) errors.Add($"批次内主键重复：{record.TableName}[{key}]");

            try
            {
                _ = MasterMemoryDatabaseService.GetRecord(databasePath, record.TableName, key);
                errors.Add($"数据库中已存在主键：{record.TableName}[{key}]");
            }
            catch (KeyNotFoundException)
            {
            }
        }

        if (draft.Records.Count == 0) errors.Add("创建批次为空。");
        if (!draft.SkipResourceChecks)
        {
            foreach (var record in draft.Records)
            foreach (var pair in record.Fields)
            {
                if (!pair.Key.Contains("Asset", StringComparison.OrdinalIgnoreCase)
                    && !pair.Key.Contains("Path", StringComparison.OrdinalIgnoreCase)) continue;
                if (pair.Value is string path && path.Length > 0 && !Path.IsPathRooted(path))
                    warnings.Add($"资源 Key/相对路径待由发布目录解析：{record.TableName}.{pair.Key}={path}");
            }
        }

        return new MasterCreationPreview(draft.Records, errors, warnings);
    }

    public MasterDatabaseVerification Apply(string databasePath, MasterCreationDraft draft, string outputPath)
    {
        var preview = CreatePreview(databasePath, draft);
        if (!preview.IsValid)
            throw new InvalidDataException(string.Join(Environment.NewLine, preview.Errors));

        var temp = outputPath + ".creating-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(databasePath, temp, overwrite: true);
            foreach (var record in draft.Records)
            {
                var json = JsonSerializer.Serialize(CoerceFields(temp, record), JsonOptions);
                var next = temp + ".next";
                MasterMemoryDatabaseService.AddRecord(temp, record.TableName, json, next);
                File.Move(next, temp, overwrite: true);
            }

            var verification = MasterMemoryDatabaseService.Verify(temp);
            File.Move(temp, outputPath, overwrite: true);
            return verification;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            var next = temp + ".next";
            if (File.Exists(next)) File.Delete(next);
        }
    }

    private static IReadOnlyDictionary<string, object?> CoerceFields(string databasePath, MasterCreationRecord record)
    {
        var schema = MasterMemoryDatabaseService.GetSchema(databasePath, record.TableName)
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in record.Fields)
        {
            if (pair.Value is not string text || !schema.TryGetValue(pair.Key, out var property))
            {
                result[pair.Key] = pair.Value;
                continue;
            }
            var type = property.Type;
            if (string.IsNullOrWhiteSpace(text)) { result[pair.Key] = null; continue; }
            if (type is "Boolean" or "bool") result[pair.Key] = bool.Parse(text);
            else if (type.Contains("Int", StringComparison.OrdinalIgnoreCase) || type.Contains("Long", StringComparison.OrdinalIgnoreCase))
                result[pair.Key] = long.Parse(text, CultureInfo.InvariantCulture);
            else if (type.Contains("Single", StringComparison.OrdinalIgnoreCase) || type.Contains("Double", StringComparison.OrdinalIgnoreCase) || type.Contains("Decimal", StringComparison.OrdinalIgnoreCase))
                result[pair.Key] = double.Parse(text, CultureInfo.InvariantCulture);
            else if (type.Contains("DateTime", StringComparison.OrdinalIgnoreCase))
                result[pair.Key] = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            else result[pair.Key] = text;
        }
        return result;
    }
}
