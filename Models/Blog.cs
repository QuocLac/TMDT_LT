using System;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models
{
    public class Blog
    {
        [Key]
        public int BlogId { get; set; }

        [Required(ErrorMessage = "Tiêu đề bài viết là bắt buộc")]
        [StringLength(200, ErrorMessage = "Tiêu đề không được vượt quá 200 ký tự")]
        public string Title { get; set; } = null!;

        [Required]
        [StringLength(250)]
        // Slug dùng để tạo đường dẫn chuẩn SEO (VD: /blog/apple-ra-mat-iphone-18)
        public string Slug { get; set; } = null!;

        [StringLength(500, ErrorMessage = "Mô tả ngắn tối đa 500 ký tự")]
        public string? ShortDescription { get; set; } // Dòng text mồi nhử hiển thị ở thẻ Card danh sách

        public string? Thumbnail { get; set; } // Ảnh đại diện cho thẻ Card (Nên dùng tỷ lệ 4:3 hoặc 16:9)

        public string? BackgroundImage { get; set; } // Ảnh Parallax khổ lớn trên cùng của bài. (Nếu NULL -> Kích hoạt CSS xanh-trắng quét sáng)

        public string? HtmlContent { get; set; } // Lưu trữ toàn bộ mã HTML sinh ra từ công cụ soạn thảo của Admin

        public string? CustomCss { get; set; } // Ô để Admin tự do gắn mã CSS/Animation cho riêng bài viết này (Ví dụ: keyframes quét sáng)

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? PublishedAt { get; set; }

        public bool IsActive { get; set; } = true;

        public int ViewCount { get; set; } = 0; // Bộ đếm lượt xem để làm tính năng "Bài viết thịnh hành"
        // Cầu nối quan trọng để sửa lỗi CS1061
        public virtual ICollection<BlogMedia> BlogMedias { get; set; } = new List<BlogMedia>();
    }
}