namespace TMDT_LT.Services.Inventory.Contracts;

public sealed class CreateInventoryCountRequest
{
    public int WarehouseId { get; set; }
    public string? ScopeType { get; set; }
    public string? Notes { get; set; }
    public List<int> VariantIds { get; set; } = new();
}

public sealed class SaveInventoryCountLineRequest
{
    public int CountedQuantity { get; set; }
    public string? ReasonCode { get; set; }
    public decimal? AdjustmentUnitCost { get; set; }
    public string? Note { get; set; }
}

public sealed class CancelInventoryCountRequest
{
    public string? Reason { get; set; }
}
