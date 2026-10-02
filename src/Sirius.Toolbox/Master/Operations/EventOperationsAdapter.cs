namespace Sirius.Toolbox.Master.Operations;

public sealed class EventOperationsAdapter(MasterOperationsService service)
{
    public IReadOnlyList<MasterBusinessRow> List(string databasePath, string? search = null)
        => service.ListRows(databasePath, "EventMaster", search);

    public MasterOperationPlan Extend(string databasePath, string key, DateTimeOffset targetEnd, bool includeRelated)
        => service.CreateTimedExtensionPlan(databasePath, "EventMaster", key, targetEnd, includeRelated);
}
