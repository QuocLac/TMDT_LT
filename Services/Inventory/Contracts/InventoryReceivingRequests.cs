namespace TMDT_LT.Services.Inventory.Contracts;

public sealed class InventoryReceivingRequest
{
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal OtherFee { get; set; }
    public bool InputVatDeductible { get; set; } = true;
    public string? Note { get; set; }
    public List<InventoryReceivingLineRequest> Items { get; set; } = new();
}

public sealed class InventoryReceivingLineRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal ImportPrice { get; set; }
    public decimal TaxRate { get; set; }
}
