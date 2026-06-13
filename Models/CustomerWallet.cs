using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class CustomerWallet
    {
        [Key]
        public int WalletId { get; set; }

        [Required]
        public int CustomerId { get; set; }
        [ForeignKey("CustomerId")]
        public virtual Customer? Customer { get; set; }

        [Required]
        public int PromotionId { get; set; }
        [ForeignKey("PromotionId")]
        public virtual Promotions? Promotion { get; set; }

        [Required]
        [Range(0, 2)]
        // 0 = Đã lưu (Saved) nhưng chưa dùng
        // 1 = Đã sử dụng (Used)
        // 2 = Đã hết hạn (Expired)
        public int Status { get; set; } = 0;

        public DateTime SavedAt { get; set; } = DateTime.Now;
        public DateTime? UsedAt { get; set; } // Nullable: Chỉ ghi nhận giờ khi khách chốt đơn
    }
}