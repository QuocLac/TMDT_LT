namespace TMDT_LT.Services.Inventory.Contracts;

public sealed class InventoryDistributionRequest
{
    public int StoreId { get; set; }
    public int FromWarehouseId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal ShippingCost { get; set; }
    public string? Notes { get; set; }
    public List<InventoryDistributionItemRequest> Items { get; set; } = new();
}

public sealed class InventoryDistributionItemRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal ExportPrice { get; set; }
    public decimal TaxRate { get; set; }
}
