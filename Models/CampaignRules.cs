using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class CampaignRules
    {
        [Key]
        public int RuleId { get; set; }

        public int CampaignId { get; set; }
        [ForeignKey("CampaignId")]
        public virtual Campaigns? Campaign { get; set; }

        [Required]
        [StringLength(50)]
        // Loại mục tiêu: "Category" (Danh mục), "Brand" (Hãng), "Product" (Sản phẩm), "All" (Toàn sàn)
        public string TargetType { get; set; } = string.Empty;

        // ID của Danh mục/Hãng/Sản phẩm (Để null nếu TargetType là "All")
        public int? TargetId { get; set; }

        [Required]
        [StringLength(50)]
        // Loại giảm: "Percentage" (Giảm %), "FixedAmount" (Giảm tiền mặt)
        public string DiscountType { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountValue { get; set; }
    }
}