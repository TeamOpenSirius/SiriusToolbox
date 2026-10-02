namespace Sirius.Toolbox.Master.Creation;

public sealed record MasterCreationRecord(
    string TableName,
    IReadOnlyDictionary<string, object?> Fields,
    string Description = "");

public sealed record MasterCreationDraft(
    string Kind,
    IReadOnlyList<MasterCreationRecord> Records,
    bool SkipResourceChecks = false);

public sealed record MasterCreationPreview(
    IReadOnlyList<MasterCreationRecord> Records,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}
