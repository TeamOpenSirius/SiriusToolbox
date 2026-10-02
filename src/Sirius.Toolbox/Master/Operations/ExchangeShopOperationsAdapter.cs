namespace Sirius.Toolbox.Master.Operations;

public sealed class ExchangeShopOperationsAdapter(MasterOperationsService service)
{
    public IReadOnlyList<MasterBusinessRow> List(string databasePath, string? search = null)
        => service.ListRows(databasePath, "ExchangeShopMaster", search);

    public IReadOnlyList<MasterNestedRow> ListItems(string databasePath, string shopKey)
        => service.ListNestedRows(databasePath, "ExchangeShopMaster", shopKey);

    public MasterOperationPlan ExtendShop(string databasePath, string shopKey, DateTimeOffset targetEnd, bool includeItems)
        => includeItems
            ? service.CreateNestedExtensionPlan(databasePath, "ExchangeShopMaster", shopKey, targetEnd)
            : service.CreateTimedExtensionPlan(databasePath, "ExchangeShopMaster", shopKey, targetEnd, includeRelated: false);

    public MasterOperationPlan ExtendItem(string databasePath, string shopKey, string itemKey, DateTimeOffset targetEnd)
        => service.CreateNestedExtensionPlan(databasePath, "ExchangeShopMaster", shopKey, targetEnd, itemKey);
}
