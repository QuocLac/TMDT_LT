using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public static class InventoryLotSourceTypes
    {
        public const string Purchase = "Purchase";
        public const string CountAdjustment = "CountAdjustment";
        public const string CustomerReturn = "CustomerReturn";
        public const string WarehouseTransfer = "WarehouseTransfer";
    }

    public class InventoryLots
    {
        public int LotId { get; set; }

        // Nullable để lô điều chỉnh kiểm kê không bị gắn giả vào một phiếu nhập/NCC.
        public int? Poid { get; set; }

        public int VariantId { get; set; }

        public int? SupplierId { get; set; }

        public int WarehouseId { get; set; } = 1;

        public int ReceivedQuantity { get; set; }

        public int RemainingQuantity { get; set; }

        public decimal UnitCost { get; set; }

        public DateTime ReceivedDate { get; set; } = DateTime.Now;

        public bool IsDeleted { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(30)]
        public string? SourceType { get; set; } = InventoryLotSourceTypes.Purchase;

        [MaxLength(80)]
        public string? SourceReference { get; set; }

        public int? InventoryCountLineId { get; set; }

        public virtual ProductVariants? Variant { get; set; }

        public virtual Warehouses? Warehouse { get; set; }

        [ForeignKey(nameof(InventoryCountLineId))]
        [InverseProperty(nameof(InventoryCountLines.AdjustmentLots))]
        public virtual InventoryCountLines? AdjustmentLine { get; set; }

        public virtual ICollection<ProductSerials> ProductSerials { get; set; }
            = new List<ProductSerials>();

        public virtual ICollection<OrderInventoryAllocations> OrderInventoryAllocations { get; set; }
            = new List<OrderInventoryAllocations>();
    }
}
