using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models;

public partial class ReviewDetails
{
    [Key]
    public int ReviewDetailId { get; set; }

    // Foreign Keys
    public int? ReviewId { get; set; }
    public int? VariantId { get; set; }

    [Range(1, 5, ErrorMessage = "Đánh giá phải từ 1 đến 5 sao")]
    public int? Rating { get; set; }

    public string? Comment { get; set; }

    // --- CỘT MỚI: LƯU HÌNH ẢNH/VIDEO TỪ NGƯỜI DÙNG ---
    public string? MediaUrls { get; set; }

    // --- CÁC TRƯỜNG NGHIỆP VỤ QUẢN TRỊ (SAAS REVIEW MANAGEMENT) ---

    // Cờ trạng thái: Đánh dấu bình luận mới/chưa đọc để hiện Badge đỏ
    public bool IsRead { get; set; } = false;

    // Cờ trạng thái: Ẩn đánh giá (Soft Delete) khỏi giao diện khách hàng
    public bool IsHidden { get; set; } = false;

    // Nội dung: Lời phản hồi/cảm ơn từ Admin
    public string? AdminReply { get; set; }

    // Mốc thời gian: Dùng để sort sản phẩm có đánh giá mới nhất lên đầu danh sách
    public DateTime? CreatedAt { get; set; } = DateTime.Now;



    // --- NAVIGATION PROPERTIES (XỬ LÝ LỖI INCLUDE) ---

    [ForeignKey("ReviewId")]
    public virtual Reviews? Review { get; set; }

    [ForeignKey("VariantId")]
    public virtual ProductVariants? Variant { get; set; }
}