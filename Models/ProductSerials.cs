using System;

namespace TMDT_LT.Models
{
    public class ProductSerials
    {
        public int SerialId { get; set; }
        public int VariantId { get; set; }
        public int LotId { get; set; }
        public string SerialNumber { get; set; } = null!; // Mã định danh IMEI/Serial duy nhất
        public string Status { get; set; } = "InStock"; // InStock, Reserved, Sold, Damaged
        public int? OrderId { get; set; } // Khóa ngoại nối với đơn xuất hàng nếu có
        public DateTime? SoldDate { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public virtual ProductVariants? Variant { get; set; }
        public virtual InventoryLots? Lot { get; set; }
    }
}