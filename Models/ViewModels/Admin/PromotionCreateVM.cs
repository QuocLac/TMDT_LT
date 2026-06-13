using System;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels.Admin
{
    public class PromotionCreateVM
    {
        // --- 1. THÔNG TIN CHIẾN DỊCH ---
        [Required(ErrorMessage = "Mã khuyến mãi là bắt buộc")]
        [RegularExpression(@"^[A-Z0-9]+$", ErrorMessage = "Mã chỉ chứa chữ in hoa và số, không khoảng trắng")]
        [StringLength(20)]
        public string Code { get; set; } = null!;

        [Required(ErrorMessage = "Tên chương trình là bắt buộc")]
        [StringLength(150)]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ngày bắt đầu")]
        public DateTime StartDate { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "Vui lòng chọn ngày kết thúc")]
        public DateTime EndDate { get; set; } = DateTime.Now.AddDays(7);

        [Range(1, 100000, ErrorMessage = "Giới hạn sử dụng phải lớn hơn 0")]
        public int UsageLimit { get; set; } = 100;

        // --- 2. ĐIỀU KIỆN & PHẦN THƯỞNG ---
        [Range(0, double.MaxValue, ErrorMessage = "Giá trị tối thiểu không hợp lệ")]
        public decimal MinOrderValue { get; set; } = 0;

        [Required]
        public int DiscountType { get; set; } // 0: Giảm %, 1: Giảm VNĐ

        [Required(ErrorMessage = "Vui lòng nhập mức giảm")]
        [Range(0, double.MaxValue, ErrorMessage = "Mức giảm không được âm")]
        public decimal DiscountValue { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Mức giảm tối đa không hợp lệ")]
        public decimal? MaxDiscountAmount { get; set; }

        // --- 3. ĐỐI TƯỢNG VÀ ĐỘNG CƠ PHÂN PHỐI (AIRDROP) ---
        [Required]
        public int TargetAudience { get; set; } = 0;

        // Đã đổi sang kiểu String để khớp với cột CustomerType trong CSDL
        public string? TargetCustomerType { get; set; }

        // Danh sách Email đích danh
        public string? TargetEmails { get; set; }
    }
}