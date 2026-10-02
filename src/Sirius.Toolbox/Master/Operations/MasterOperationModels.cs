using System.Text.Json;

namespace Sirius.Toolbox.Master.Operations;

public sealed record MasterOperationPatch(
    string Table,
    string Key,
    string PatchJson,
    string Description);

public sealed record MasterOperationApplyResult(
    string DatabasePath,
    int AppliedChanges,
    string Sha256);

public sealed record MasterBusinessRow(
    string Table,
    string Key,
    string DisplayName,
    long? Id,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    DateTimeOffset? ForceEnd,
    IReadOnlyDictionary<string, object?> Values)
{
    public string Status(DateTimeOffset now)
    {
        var effectiveEnd = ForceEnd ?? End;
        if (Start is not null && now < Start.Value) return "未开始";
        if (effectiveEnd is not null && now > effectiveEnd.Value) return "已结束";
        return "进行中";
    }
}

public sealed record MasterNestedRow(
    string ParentTable,
    string ParentKey,
    string Path,
    string Key,
    string DisplayName,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    IReadOnlyDictionary<string, object?> Values);

public sealed class MasterOperationPlan
{
    private readonly List<MasterOperationPatch> _changes = [];

    public MasterOperationPlan(string title)
    {
        Title = title;
    }

    public string Title { get; }
    public IReadOnlyList<MasterOperationPatch> Changes => _changes;
    public bool IsEmpty => _changes.Count == 0;

    public void Add(string table, string key, IReadOnlyDictionary<string, object?> patch, string description)
    {
        if (patch.Count == 0) return;
        _changes.Add(new MasterOperationPatch(
            table,
            key,
            JsonSerializer.Serialize(patch, MasterOperationJson.JsonOptions),
            description));
    }

    public void AddJson(string table, string key, string patchJson, string description)
    {
        using var document = JsonDocument.Parse(patchJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Master operation patch must be a JSON object.");
        if (!document.RootElement.EnumerateObject().Any()) return;
        _changes.Add(new MasterOperationPatch(table, key, patchJson, description));
    }

    public string ToPreviewText(int maxLines = 120)
    {
        if (_changes.Count == 0) return "没有需要修改的数据。";

        var lines = new List<string>
        {
            Title,
            $"共 {_changes.Count} 条记录变更：",
            string.Empty
        };
        foreach (var change in _changes.Take(Math.Max(1, maxLines - 4)))
            lines.Add($"- {change.Table} [{change.Key}]  {change.Description}");
        if (_changes.Count > maxLines - 4)
            lines.Add($"... 其余 {_changes.Count - (maxLines - 4)} 条未显示");
        return string.Join(Environment.NewLine, lines);
    }
}
