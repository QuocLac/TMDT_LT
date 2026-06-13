using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class Promotions
    {
        [Key]
        public int PromotionId { get; set; }

        [Required(ErrorMessage = "Mã khuyến mãi là bắt buộc")]
        [StringLength(20, ErrorMessage = "Mã không được vượt quá 20 ký tự")]
        [RegularExpression(@"^[A-Z0-9]+$", ErrorMessage = "Mã khuyến mãi chỉ được chứa chữ cái in hoa và số, không khoảng trắng")]
        public string Code { get; set; } = null!;

        [Required(ErrorMessage = "Tên chương trình là bắt buộc")]
        [StringLength(150)]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        [Required(ErrorMessage = "Ngày bắt đầu là bắt buộc")]
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "Ngày kết thúc là bắt buộc")]
        public DateTime EndDate { get; set; }

        [Range(1, 100000, ErrorMessage = "Giới hạn sử dụng phải từ 1 đến 100,000")]
        public int UsageLimit { get; set; } // Tổng số lần mã có thể được sử dụng trên toàn sàn

        public int UsedCount { get; set; } = 0; // Bộ đếm số lần đã dùng thực tế

        public bool IsActive { get; set; } = true;

        // ==============================================================
        // BỔ SUNG TRƯỜNG LƯU TRỮ PHÂN KHÚC ĐỐI TƯỢNG (Khắc phục lỗi)
        // 0: Công khai | 1: Toàn sàn | 2: Phân khúc | 3: Đích danh
        // ==============================================================
        public int TargetAudience { get; set; } = 0;

        // Navigation Properties
        public virtual ICollection<PromotionRules> PromotionRules { get; set; } = new List<PromotionRules>();
        public virtual ICollection<CustomerWallet> CustomerWallets { get; set; } = new List<CustomerWallet>();
    }
}