using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class OrderReturns
    {
        [Key]
        public int ReturnId { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        [StringLength(255)]
        public string Reason { get; set; } = string.Empty;

        public string? Description { get; set; }

        // Lưu đường dẫn ảnh/video minh chứng (ngăn cách bởi dấu phẩy)
        public string? MediaUrls { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Chờ duyệt";
        // 5 MỐC CHUẨN: Chờ duyệt, Chờ khách trả hàng, Đang kiểm định, Hoàn tiền thành công, Đã từ chối

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? ResolvedAt { get; set; }

        public string? AdminNote { get; set; }

        // CỜ TRẠNG THÁI: Xác định Admin đã click vào thông báo trên trang Index chưa
        public bool IsAlertAdminRead { get; set; } = false;

        // ====================================================================
        // THÔNG TIN TÀI KHOẢN NGÂN HÀNG NHẬN TIỀN HOÀN (Dành cho quét VietQR)
        // ====================================================================
        [StringLength(50)]
        public string? RefundBankCode { get; set; } // Mã Ngân hàng (VD: VCB, MB, ACB...)

        [StringLength(50)]
        public string? RefundAccountNumber { get; set; } // Số tài khoản thụ hưởng

        [StringLength(100)]
        public string? RefundAccountName { get; set; } // Tên chủ tài khoản

        // Navigation Properties
        [ForeignKey("OrderId")]
        public virtual Orders? Order { get; set; }

        [ForeignKey("CustomerId")]
        public virtual Customer? Customer { get; set; }
    }
}