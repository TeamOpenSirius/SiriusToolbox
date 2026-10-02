namespace Sirius.Toolbox.Master.Operations;

public sealed class GachaOperationsAdapter(MasterOperationsService service)
{
    public IReadOnlyList<MasterBusinessRow> List(string databasePath, string? search = null)
        => service.ListRows(databasePath, "GachaMaster", search);

    public MasterOperationPlan Extend(string databasePath, string key, DateTimeOffset targetEnd, bool includeRelated)
        => service.CreateTimedExtensionPlan(databasePath, "GachaMaster", key, targetEnd, includeRelated);
}
