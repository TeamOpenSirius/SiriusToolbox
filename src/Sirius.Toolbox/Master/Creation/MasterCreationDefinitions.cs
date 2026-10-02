namespace Sirius.Toolbox.Master.Creation;

public static class MasterCreationDefinitions
{
    public static MasterCreationDraft Music(long id, IReadOnlyDictionary<string, object?> fields)
        => Single("音乐", "MusicMaster", id, fields);

    public static MasterCreationDraft Event(long id, IReadOnlyDictionary<string, object?> fields)
        => Single("活动", "EventMaster", id, fields);

    public static MasterCreationDraft Card(long id, long baseId, IReadOnlyDictionary<string, object?> cardFields,
        IReadOnlyDictionary<string, object?> baseFields)
        => new("卡面", [
            new MasterCreationRecord("CharacterBaseMaster", WithId(baseId, baseFields), "角色基础"),
            new MasterCreationRecord("CharacterMaster", WithId(id, cardFields, ("CharacterBaseMasterId", baseId)), "卡面")
        ]);

    private static MasterCreationDraft Single(string kind, string table, long id, IReadOnlyDictionary<string, object?> fields)
        => new(kind, [new MasterCreationRecord(table, WithId(id, fields), kind)]);

    private static IReadOnlyDictionary<string, object?> WithId(long id, IReadOnlyDictionary<string, object?> fields,
        params (string Name, object? Value)[] extra)
    {
        var result = new Dictionary<string, object?>(fields, StringComparer.OrdinalIgnoreCase) { ["Id"] = id };
        foreach (var (name, value) in extra) result[name] = value;
        return result;
    }
}
