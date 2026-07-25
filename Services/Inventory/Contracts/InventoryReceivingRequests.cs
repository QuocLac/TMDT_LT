using Microsoft.AspNetCore.Http;

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

/// <summary>
/// Tạo nhanh một sản phẩm mới kèm biến thể đầu tiên hoặc thêm biến thể vào
/// sản phẩm đang có. Giá nhập chỉ dùng để điền vào phiếu nhập hiện tại và
/// không được lưu thành giá vốn của biến thể trước khi nhận hàng.
/// </summary>
public sealed class InventoryQuickItemRequest
{
    public bool CreateNewProduct { get; set; }
    public int? ProductId { get; set; }
    public string? ProductName { get; set; }
    public int? CategoryId { get; set; }
    public int? BrandId { get; set; }
    public string? Color { get; set; }
    public string? Storage { get; set; }
    public string? Ram { get; set; }
    public decimal ListPrice { get; set; }
    public decimal InitialImportPrice { get; set; }
    public string? ImageUrl { get; set; }
    public IFormFile? ImageFile { get; set; }
}
