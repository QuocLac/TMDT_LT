using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class PromotionRules
    {
        [Key]
        public int RuleId { get; set; }

        [Required]
        public int PromotionId { get; set; }
        [ForeignKey("PromotionId")]
        public virtual Promotions? Promotion { get; set; }

        // --- ĐIỀU KIỆN ÁP DỤNG (CONDITIONS) ---

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Giá trị đơn hàng tối thiểu không được âm")]
        public decimal MinOrderValue { get; set; } = 0; // Khách phải mua tối thiểu bao nhiêu tiền?

        // Có thể mở rộng sau: Áp dụng riêng cho danh mục/thương hiệu nào đó
        public int? RequiredCategoryId { get; set; }
        public int? RequiredBrandId { get; set; }

        // --- PHẦN THƯỞNG (REWARDS) ---

        [Required]
        [Range(0, 2, ErrorMessage = "Loại giảm giá không hợp lệ")]
        // 0 = Giảm theo % (Percentage)
        // 1 = Giảm tiền trực tiếp (Fixed Amount)
        // 2 = Miễn phí vận chuyển (Free Shipping)
        public int DiscountType { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Mức giảm không được âm")]
        public decimal DiscountValue { get; set; } // Nếu Type=0 thì đây là %, nếu Type=1 thì đây là VNĐ

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, double.MaxValue)]
        public decimal? MaxDiscountAmount { get; set; } // Cực kỳ quan trọng: Mức giảm tối đa (Tránh việc giảm 10% cho đơn 1 tỷ gây lỗ vốn)
    }
}