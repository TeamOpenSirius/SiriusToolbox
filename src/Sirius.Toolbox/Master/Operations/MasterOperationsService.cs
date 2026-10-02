using System.Text.Json;
using System.Text.Json.Nodes;
using Sirius.MasterData;

namespace Sirius.Toolbox.Master.Operations;

public sealed class MasterOperationsService
{
    private const int PageSize = 1000;

    public IReadOnlyList<MasterBusinessRow> ListRows(string databasePath, string tableName, string? search = null)
    {
        var summary = MasterMemoryDatabaseService.GetTables(databasePath)
            .FirstOrDefault(x => string.Equals(x.Name, tableName, StringComparison.Ordinal));
        if (summary is null) return [];

        var result = new List<MasterBusinessRow>(summary.RowCount);
        for (var offset = 0; offset < summary.RowCount; offset += PageSize)
        {
            foreach (var record in MasterMemoryDatabaseService.ListRecords(databasePath, tableName, offset, PageSize))
            {
                var values = MasterOperationJson.RequireMap(record.Record, tableName, record.PrimaryKey);
                var row = CreateBusinessRow(tableName, record.PrimaryKey, values);
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim();
                    if (!row.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                        && !row.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                result.Add(row);
            }
        }
        return result;
    }

    public MasterBusinessRow GetRow(string databasePath, string tableName, string key)
    {
        var record = MasterMemoryDatabaseService.GetRecord(databasePath, tableName, key);
        var values = MasterOperationJson.RequireMap(record.Record, tableName, key);
        return CreateBusinessRow(tableName, key, values);
    }

    public MasterOperationPlan CreateRecordPatchPlan(
        string table,
        string key,
        IReadOnlyDictionary<string, object?> patch,
        string description)
    {
        var plan = new MasterOperationPlan(description);
        plan.Add(table, key, patch, description);
        return plan;
    }

    public MasterOperationPlan CreateTimedExtensionPlan(
        string databasePath,
        string tableName,
        string key,
        DateTimeOffset targetEnd,
        bool includeRelated)
    {
        var selected = GetRow(databasePath, tableName, key);
        var plan = new MasterOperationPlan(
            includeRelated
                ? $"延长 {selected.DisplayName} 及关联 MasterData 到 {targetEnd:u}"
                : $"延长 {selected.DisplayName} 到 {targetEnd:u}");

        var ownPatch = MasterOperationJson.CreateEndPatch(selected.Values, targetEnd);
        plan.Add(tableName, key, ownPatch, $"结束时间 -> {targetEnd:u}");

        if (!includeRelated || selected.Id is null) return plan;

        var baseName = BaseName(tableName);
        foreach (var related in FindRelatedRows(databasePath, tableName, selected.Key, selected.Id.Value))
        {
            var patchJson = CreateRecursiveEndPatchJson(related.Values, targetEnd, out var changedFields);
            if (changedFields == 0) continue;
            plan.AddJson(related.Table, related.Key, patchJson,
                $"关联 {baseName}Id={selected.Id}: 结束时间 -> {targetEnd:u}（{changedFields} 个字段）");
        }

        return plan;
    }

    public MasterOperationPlan CreateTimedUpdatePlan(
        string databasePath,
        string tableName,
        string key,
        string? name,
        DateTimeOffset? start,
        DateTimeOffset? end,
        DateTimeOffset? forceEnd,
        bool includeRelated)
    {
        var selected = GetRow(databasePath, tableName, key);
        var plan = new MasterOperationPlan(includeRelated
            ? $"编辑 {selected.DisplayName} 并同步关联时间"
            : $"编辑 {selected.DisplayName}");
        var ownPatch = CreateCommonFieldPatch(selected, name, start, end, forceEnd);
        plan.Add(tableName, key, ownPatch, "更新主记录基本字段");

        if (!includeRelated || selected.Id is null) return plan;

        var baseName = BaseName(tableName);
        foreach (var related in FindRelatedRows(databasePath, tableName, selected.Key, selected.Id.Value))
        {
            var patch = new Dictionary<string, object?>(StringComparer.Ordinal);
            AddIfPresent(related.Values, patch, start, "StartDate", "StartTime", "StartAt");
            AddIfPresent(related.Values, patch, end, "EndDate", "EndTime", "EndAt");
            AddIfPresent(related.Values, patch, forceEnd, "ForceEndDate", "ForceEndTime", "ForceEndAt");

            var effectiveEnd = forceEnd ?? end;
            if (effectiveEnd is not null)
            {
                var recursiveJson = CreateRecursiveEndPatchJson(related.Values, effectiveEnd.Value, out _);
                using var recursiveDoc = JsonDocument.Parse(recursiveJson);
                foreach (var property in recursiveDoc.RootElement.EnumerateObject())
                    patch[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
            }

            plan.Add(related.Table, related.Key, patch,
                $"同步关联 {baseName}Id={selected.Id} 时间字段");
        }
        return plan;
    }

    public IReadOnlyList<MasterNestedRow> ListNestedRows(string databasePath, string tableName, string key)
    {
        var parent = GetRow(databasePath, tableName, key);
        var root = MasterOperationJson.ToJsonObject(parent.Values);
        var rows = new List<MasterNestedRow>();
        foreach (var (path, value) in MasterOperationJson.EnumerateNestedObjects(root))
        {
            var map = JsonObjectToNormalizedMap(value);
            var id = MasterOperationJson.GetInt64(map, "Id", "ExchangeShopThingId", "ThingId");
            var itemKey = id?.ToString() ?? path;
            var name = MasterOperationJson.DisplayName(map, itemKey);
            var start = MasterOperationJson.GetDate(map, "StartDate", "StartTime", "StartAt");
            var end = MasterOperationJson.GetDate(map, "EndDate", "AdditionalEndDate", "EndTime", "EndAt");
            rows.Add(new MasterNestedRow(tableName, key, path, itemKey, name, start, end, map));
        }
        return rows;
    }

    public MasterOperationPlan CreateNestedExtensionPlan(
        string databasePath,
        string tableName,
        string key,
        DateTimeOffset targetEnd,
        string? nestedKey = null)
    {
        var parent = GetRow(databasePath, tableName, key);
        var root = MasterOperationJson.ToJsonObject(parent.Values);
        var patch = new JsonObject();
        var changed = 0;

        foreach (var pair in root.ToArray())
        {
            if (pair.Value is not JsonArray array) continue;
            var cloned = array.DeepClone() as JsonArray ?? new JsonArray();
            var arrayChanged = 0;
            for (var i = 0; i < cloned.Count; i++)
            {
                if (cloned[i] is not JsonObject item) continue;
                if (nestedKey is not null && !NestedObjectMatchesKey(item, nestedKey)) continue;
                arrayChanged += MasterOperationJson.ReplaceNestedEndDates(item, targetEnd);
            }
            if (arrayChanged <= 0) continue;
            patch[pair.Key] = cloned;
            changed += arrayChanged;
        }

        var ownEndPatch = MasterOperationJson.CreateEndPatch(parent.Values, targetEnd);
        foreach (var pair in ownEndPatch)
            patch[pair.Key] = JsonSerializer.SerializeToNode(pair.Value, MasterOperationJson.JsonOptions);

        var title = nestedKey is null
            ? $"延长交换商店 {parent.DisplayName} 与全部商品"
            : $"延长交换商品 {nestedKey}";
        var plan = new MasterOperationPlan(title);
        if (patch.Count != 0)
        {
            plan.AddJson(tableName, key, patch.ToJsonString(MasterOperationJson.JsonOptions),
                nestedKey is null
                    ? $"商店及嵌套商品结束时间 -> {targetEnd:u}（{changed} 个嵌套字段）"
                    : $"商品 {nestedKey} 结束时间 -> {targetEnd:u}");
        }
        return plan;
    }

    public MasterOperationPlan CreateMusicUnlockPlan(
        string databasePath,
        IEnumerable<string>? keys,
        bool onlyPurchaseLocked,
        int targetUnlockType = 1)
    {
        const string tableName = "MusicMaster";
        var selectedKeys = keys?.ToHashSet(StringComparer.Ordinal);
        var rows = ListRows(databasePath, tableName);
        var plan = new MasterOperationPlan(
            targetUnlockType == 1 ? "将乐曲设为默认解锁" : $"修改乐曲解锁类型为 {targetUnlockType}");

        foreach (var row in rows)
        {
            if (selectedKeys is not null && !selectedKeys.Contains(row.Key)) continue;
            var unlockProperty = FindUnlockTypeProperty(row.Values);
            if (unlockProperty is null) continue;

            var oldType = MasterOperationJson.ToInt64(row.Values[unlockProperty]);
            if (onlyPurchaseLocked && oldType is not (10 or 11)) continue;
            if (oldType == targetUnlockType) continue;

            var patch = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [unlockProperty] = targetUnlockType
            };
            foreach (var name in row.Values.Keys)
            {
                if (name.Contains("Unlock", StringComparison.OrdinalIgnoreCase)
                    && (name.Contains("Value", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Text", StringComparison.OrdinalIgnoreCase)))
                {
                    patch[name] = null;
                }
            }
            plan.Add(tableName, row.Key, patch,
                $"{row.DisplayName}: unlock {oldType?.ToString() ?? "?"} -> {targetUnlockType}");
        }
        return plan;
    }

    public IReadOnlyDictionary<string, object?> CreateCommonFieldPatch(
        MasterBusinessRow row,
        string? name,
        DateTimeOffset? start,
        DateTimeOffset? end,
        DateTimeOffset? forceEnd)
    {
        var patch = new Dictionary<string, object?>(StringComparer.Ordinal);
        var nameProperty = MasterOperationJson.FindProperty(row.Values,
            "Name", "DisplayName", "Title", "EventName", "GachaName", "ShopName", "MusicName");
        if (nameProperty is not null && name is not null)
            patch[nameProperty] = name;

        var startProperty = MasterOperationJson.FindProperty(row.Values, "StartDate", "StartTime", "StartAt");
        if (startProperty is not null && start is not null)
            patch[startProperty] = start.Value;

        var endProperty = MasterOperationJson.FindProperty(row.Values, "EndDate", "EndTime", "EndAt");
        if (endProperty is not null && end is not null)
            patch[endProperty] = end.Value;

        var forceProperty = MasterOperationJson.FindProperty(row.Values, "ForceEndDate", "ForceEndTime", "ForceEndAt");
        if (forceProperty is not null && forceEnd is not null)
            patch[forceProperty] = forceEnd.Value;

        return patch;
    }

    public static string? FindUnlockTypeProperty(IReadOnlyDictionary<string, object?> values)
    {
        return MasterOperationJson.FindProperty(values,
                   "UnlockConditionType", "MusicUnlockConditionType", "MusicUnlockConditionTypes")
               ?? MasterOperationJson.FindPropertyContaining(values, "Unlock", "Type");
    }

    public static string? FindUnlockTextProperty(IReadOnlyDictionary<string, object?> values)
        => MasterOperationJson.FindProperty(values, "UnlockText")
           ?? MasterOperationJson.FindPropertyContaining(values, "Unlock", "Text");

    public MasterOperationApplyResult Apply(string databasePath, MasterOperationPlan plan)
        => new MasterOperationApplier().Apply(databasePath, plan);



    private static string CreateRecursiveEndPatchJson(
        IReadOnlyDictionary<string, object?> values,
        DateTimeOffset targetEnd,
        out int changedFields)
    {
        var root = MasterOperationJson.ToJsonObject(values);
        var patch = new JsonObject();
        changedFields = 0;

        foreach (var pair in root.ToArray())
        {
            if (MasterOperationJson.IsEndProperty(pair.Key)
                && MasterOperationJson.GetDate(values, pair.Key) is not null)
            {
                patch[pair.Key] = targetEnd;
                changedFields++;
                continue;
            }

            if (pair.Value is not (JsonArray or JsonObject)) continue;
            var clone = pair.Value.DeepClone();
            var nestedChanged = MasterOperationJson.ReplaceNestedEndDates(clone, targetEnd);
            if (nestedChanged == 0) continue;
            patch[pair.Key] = clone;
            changedFields += nestedChanged;
        }

        return patch.ToJsonString(MasterOperationJson.JsonOptions);
    }

    private static string BaseName(string tableName)
        => tableName.EndsWith("Master", StringComparison.Ordinal)
            ? tableName[..^"Master".Length]
            : tableName;

    private IEnumerable<(string Table, string Key, IReadOnlyDictionary<string, object?> Values)> FindRelatedRows(
        string databasePath,
        string tableName,
        string selectedKey,
        long selectedId)
    {
        var tables = MasterMemoryDatabaseService.GetTables(databasePath);
        var tableMap = tables.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Table, string Key, long? Id, IReadOnlyDictionary<string, object?> Values, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var root = GetRow(databasePath, tableName, selectedKey);
        queue.Enqueue((tableName, root.Key, root.Id, root.Values, 0));
        visited.Add(tableName + "\u001f" + root.Key);

        const int maxDepth = 3;
        while (queue.Count != 0)
        {
            var current = queue.Dequeue();
            if (current.Depth >= maxDepth) continue;

            // Follow outgoing foreign keys such as ExchangeShopMasterId -> ExchangeShopMaster.
            foreach (var pair in current.Values)
            {
                if (!pair.Key.EndsWith("MasterId", StringComparison.OrdinalIgnoreCase)) continue;
                var foreignId = MasterOperationJson.ToInt64(pair.Value);
                if (foreignId is null) continue;
                var targetTable = pair.Key[..^"Id".Length];
                if (!tableMap.ContainsKey(targetTable)) continue;
                if (!TryGetRecordByNumericKey(databasePath, targetTable, foreignId.Value, out var target)) continue;
                var identity = targetTable + "\u001f" + target.Key;
                if (!visited.Add(identity)) continue;
                queue.Enqueue((targetTable, target.Key, target.Id, target.Values, current.Depth + 1));
                yield return (targetTable, target.Key, target.Values);
            }

            if (current.Id is null) continue;
            var baseName = BaseName(current.Table);
            var referenceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                baseName + "Id",
                baseName + "MasterId",
                current.Table + "Id"
            };

            // Follow incoming references such as EventBoxGachaMaster.EventMasterId.
            foreach (var table in tables)
            {
                if (string.Equals(table.Name, current.Table, StringComparison.OrdinalIgnoreCase)) continue;
                var schema = MasterMemoryDatabaseService.GetSchema(databasePath, table.Name);
                var relationProperties = schema
                    .Where(property => referenceNames.Contains(property.Name))
                    .Select(property => property.Name)
                    .ToArray();
                if (relationProperties.Length == 0) continue;

                foreach (var record in EnumerateRecords(databasePath, table))
                {
                    var values = MasterOperationJson.RequireMap(record.Record, table.Name, record.PrimaryKey);
                    if (!relationProperties.Any(property =>
                            values.TryGetValue(property, out var value)
                            && MasterOperationJson.ToInt64(value) == current.Id.Value))
                        continue;
                    var identity = table.Name + "\u001f" + record.PrimaryKey;
                    if (!visited.Add(identity)) continue;
                    var id = MasterOperationJson.GetInt64(values, "Id", table.Name + "Id");
                    queue.Enqueue((table.Name, record.PrimaryKey, id, values, current.Depth + 1));
                    yield return (table.Name, record.PrimaryKey, values);
                }
            }
        }
    }

    private bool TryGetRecordByNumericKey(
        string databasePath,
        string tableName,
        long id,
        out MasterBusinessRow row)
    {
        try
        {
            row = GetRow(databasePath, tableName, id.ToString());
            return true;
        }
        catch (KeyNotFoundException)
        {
            row = null!;
            return false;
        }
        catch (InvalidDataException)
        {
            row = null!;
            return false;
        }
        catch (ArgumentException)
        {
            row = null!;
            return false;
        }
        catch (InvalidOperationException)
        {
            row = null!;
            return false;
        }
    }

    private static void AddIfPresent(
        IReadOnlyDictionary<string, object?> source,
        IDictionary<string, object?> patch,
        DateTimeOffset? value,
        params string[] candidates)
    {
        if (value is null) return;
        var property = MasterOperationJson.FindProperty(source, candidates);
        if (property is not null) patch[property] = value.Value;
    }

    private static MasterBusinessRow CreateBusinessRow(
        string tableName,
        string key,
        IReadOnlyDictionary<string, object?> values)
    {
        var id = MasterOperationJson.GetInt64(values, "Id", tableName + "Id");
        var display = MasterOperationJson.DisplayName(values, id?.ToString() ?? key);
        var start = MasterOperationJson.GetDate(values, "StartDate", "StartTime", "StartAt");
        var end = MasterOperationJson.GetDate(values, "EndDate", "EndTime", "EndAt", "AdditionalEndDate");
        var forceEnd = MasterOperationJson.GetDate(values, "ForceEndDate", "ForceEndTime", "ForceEndAt");
        return new MasterBusinessRow(tableName, key, display, id, start, end, forceEnd, values);
    }

    private static IEnumerable<MasterRecordView> EnumerateRecords(string databasePath, MasterTableSummary table)
    {
        for (var offset = 0; offset < table.RowCount; offset += PageSize)
        {
            foreach (var row in MasterMemoryDatabaseService.ListRecords(databasePath, table.Name, offset, PageSize))
                yield return row;
        }
    }

    private static bool NestedObjectMatchesKey(JsonObject item, string key)
    {
        foreach (var candidate in new[] { "Id", "ExchangeShopThingId", "ThingId" })
        {
            var property = item.FirstOrDefault(x => string.Equals(x.Key, candidate, StringComparison.OrdinalIgnoreCase));
            if (property.Key is null || property.Value is null) continue;
            var text = property.Value.ToJsonString().Trim('"');
            if (string.Equals(text, key, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static IReadOnlyDictionary<string, object?> JsonObjectToNormalizedMap(JsonObject value)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in value)
        {
            if (pair.Value is null)
            {
                result[pair.Key] = null;
                continue;
            }
            using var doc = JsonDocument.Parse(pair.Value.ToJsonString());
            result[pair.Key] = MasterOperationJson.NormalizeJsonValue(doc.RootElement.Clone());
        }
        return result;
    }
}
