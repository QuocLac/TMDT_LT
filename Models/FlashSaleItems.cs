using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class FlashSaleItems
    {
        [Key]
        public int ItemId { get; set; }

        public int FlashSaleId { get; set; }
        [ForeignKey("FlashSaleId")]
        public virtual FlashSales? FlashSale { get; set; }

        public int VariantId { get; set; }
        [ForeignKey("VariantId")]
        public virtual ProductVariants? Variant { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal FlashSalePrice { get; set; } // Giá chốt hạ (Giá sốc)

        public int Quantity { get; set; } // Tổng suất Flash Sale được phép bán trong chiến dịch

        public int Sold { get; set; } = 0; // Số lượng đã chốt đơn thành công trong Flash Sale

        public int MaxPerUser { get; set; } = 0; // Giới hạn mua mỗi khách (0 = Không giới hạn)

        public virtual ICollection<OrderDetails> OrderDetails { get; set; } = new List<OrderDetails>();
        public virtual ICollection<FlashSaleChangeLogs> ChangeLogs { get; set; } = new List<FlashSaleChangeLogs>();
    }
}
