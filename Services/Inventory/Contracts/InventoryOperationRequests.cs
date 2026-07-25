namespace TMDT_LT.Services.Inventory.Contracts;

public sealed class InventoryTransferRequest
{
    public int SourceWarehouseId { get; set; }
    public int TargetWarehouseId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<InventoryTransferItemRequest> Items { get; set; } = new();
}

public sealed class InventoryTransferItemRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; }
}

public sealed class InventorySnapshotRepairRequest
{
    public string Reason { get; set; } = string.Empty;
}
