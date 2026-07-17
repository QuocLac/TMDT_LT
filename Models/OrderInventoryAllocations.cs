using System;

namespace TMDT_LT.Models;

public static class OrderInventoryAllocationStatuses
{
    public const string Consumed = "Consumed";
    public const string Restored = "Restored";
    public const string Damaged = "Damaged";
}

public sealed class OrderInventoryAllocations
{
    public long AllocationId { get; set; }

    public int OrderId { get; set; }

    public int OrderDetailId { get; set; }

    public int VariantId { get; set; }

    public int LotId { get; set; }

    public int WarehouseId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitCost { get; set; }

    public decimal TotalCost { get; set; }

    public string Status { get; set; }
        = OrderInventoryAllocationStatuses.Consumed;

    public DateTime AllocatedAt { get; set; } = DateTime.Now;

    public DateTime? RestoredAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public int? RestoredLotId { get; set; }

    public string? Reason { get; set; }

    public Orders Order { get; set; } = null!;

    public OrderDetails OrderDetail { get; set; } = null!;

    public ProductVariants Variant { get; set; } = null!;

    public InventoryLots Lot { get; set; } = null!;

    public Warehouses Warehouse { get; set; } = null!;
}
