namespace Sirius.Toolbox.Master.Operations;

public sealed class MusicOperationsAdapter(MasterOperationsService service)
{
    public IReadOnlyList<MasterBusinessRow> List(string databasePath, string? search = null)
        => service.ListRows(databasePath, "MusicMaster", search);

    public MasterOperationPlan MakeDefault(string databasePath, IEnumerable<string> keys)
        => service.CreateMusicUnlockPlan(databasePath, keys, onlyPurchaseLocked: false, targetUnlockType: 1);

    public MasterOperationPlan MakeAllPurchaseLockedDefault(string databasePath)
        => service.CreateMusicUnlockPlan(databasePath, null, onlyPurchaseLocked: true, targetUnlockType: 1);
}
