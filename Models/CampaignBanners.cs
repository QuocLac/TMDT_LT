using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public partial class CampaignBanners
    {
        [Key]
        public int BannerId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên chiến dịch tiếp thị.")]
        [StringLength(255, ErrorMessage = "Tên chiến dịch không được vượt quá 255 ký tự.")]
        [Display(Name = "Tên Chiến Dịch")]
        public string Title { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng tải lên hình ảnh cho Banner.")]
        [StringLength(500, ErrorMessage = "Đường dẫn ảnh quá dài (Tối đa 500 ký tự).")]
        [Display(Name = "Hình Ảnh")]
        public string ImageUrl { get; set; } = null!;

        [StringLength(500, ErrorMessage = "Đường dẫn đích không được vượt quá 500 ký tự.")]
        [Display(Name = "Đường Dẫn Đích")]
        public string? TargetUrl { get; set; }

        [Required(ErrorMessage = "Thời gian bắt đầu là bắt buộc.")]
        [Display(Name = "Ngày Bắt Đầu")]
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "Thời gian kết thúc là bắt buộc.")]
        [Display(Name = "Ngày Kết Thúc")]
        public DateTime EndDate { get; set; }

        [Required]
        [Display(Name = "Thứ Tự Hiển Thị")]
        public int DisplayOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        // Bỏ qua không yêu cầu nhập khi tạo Form, mặc định lấy giờ hệ thống
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}