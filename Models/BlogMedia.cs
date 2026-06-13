using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TMDT_LT.Models
{
    public class BlogMedia
    {
        [Key]
        public int MediaId { get; set; }

        [Required]
        public int BlogId { get; set; }

        [ForeignKey("BlogId")]
        public virtual Blog? Blog { get; set; }

        [Required]
        // Quy ước: 0 = Hình ảnh (JPG, PNG, WEBP) | 1 = Video (MP4, WebM)
        public int MediaType { get; set; } = 0;

        [Required(ErrorMessage = "Đường dẫn tệp tin là bắt buộc")]
        public string MediaUrl { get; set; } = null!;

        [StringLength(200)]
        public string? AltText { get; set; } // Chú thích ảnh (Alt) hỗ trợ SEO

        public int DisplayOrder { get; set; } = 0; // Thứ tự hiển thị trong bộ sưu tập

        public DateTime UploadedAt { get; set; } = DateTime.Now;
    }
}