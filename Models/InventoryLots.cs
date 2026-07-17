using System;
using System.Collections.Generic;

namespace TMDT_LT.Models
{
    public class InventoryLots
    {
        public int LotId { get; set; }

        public int Poid { get; set; }

        public int VariantId { get; set; }

        public int SupplierId { get; set; }

        public int WarehouseId { get; set; } = 1;

        public int ReceivedQuantity { get; set; }

        public int RemainingQuantity { get; set; }

        public decimal UnitCost { get; set; }

        public DateTime ReceivedDate { get; set; } = DateTime.Now;

        public bool IsDeleted { get; set; }

        public bool IsActive { get; set; } = true;

        public virtual ProductVariants? Variant { get; set; }

        public virtual Warehouses? Warehouse { get; set; }

        public virtual ICollection<ProductSerials> ProductSerials { get; set; }
            = new List<ProductSerials>();

        public virtual ICollection<OrderInventoryAllocations> OrderInventoryAllocations { get; set; }
            = new List<OrderInventoryAllocations>();
    }
}
