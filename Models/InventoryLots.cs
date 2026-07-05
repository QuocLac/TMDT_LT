using System;
using System.Collections.Generic;

namespace TMDT_LT.Models
{
    public class InventoryLots
    {

        public int LotId { get; set; }
        public int Poid { get; set; } // Liên kết mã phiếu nhập kho
        public int VariantId { get; set; }
        public int SupplierId { get; set; }
        public int ReceivedQuantity { get; set; }
        public int RemainingQuantity { get; set; }
        public decimal UnitCost { get; set; } // Giá vốn gốc tính FIFO
        public DateTime ReceivedDate { get; set; } = DateTime.Now;
        public bool IsDeleted { get; set; } = false;

        public bool IsActive { get; set; } = true;

        // Định nghĩa mối quan hệ điều hướng (Navigation properties)
        public virtual ProductVariants? Variant { get; set; }
        public virtual ICollection<ProductSerials> ProductSerials { get; set; } = new List<ProductSerials>();
    }
}