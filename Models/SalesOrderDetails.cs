using System;

namespace TMDT_LT.Models
{
    public class SalesOrderDetails
    {
        public int SODetailId { get; set; }
        public int SOId { get; set; }
        public int VariantId { get; set; }
        public int LotId { get; set; } // Khóa ngoại liên kết chặt chẽ với lô hàng để truy xuất giá vốn gốc cứu dữ liệu
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; } // Giá xuất buôn bán cho cửa hàng
        public decimal TaxRate { get; set; } // Thuế suất áp dụng (0, 0.08, 0.1)
        public decimal UnitCost { get; set; } // Giá vốn lấy ra tại thời điểm đó của lô hàng (FIFO)
        public decimal TotalAmount { get; set; } // Qty * UnitPrice * (1 + TaxRate)
        public decimal TotalCost { get; set; } // Qty * UnitCost
        public decimal Profit { get; set; } // TotalAmount - TotalCost (Sự chênh lệch để tính toán lời lỗ)

        public virtual SalesOrders? SalesOrder { get; set; }
        public virtual ProductVariants? Variant { get; set; }
        public virtual InventoryLots? Lot { get; set; }
    }
}