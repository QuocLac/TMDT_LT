using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels
{
    public class ProductEditVM
    {
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Tên sản phẩm không được để trống")]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int CategoryId { get; set; }

        [Required]
        public int BrandId { get; set; }

        public string MainImage { get; set; } = string.Empty; // Lưu đường dẫn cũ
        public IFormFile? MainImageFile { get; set; }          // Nhận tệp mới nếu muốn thay đổi

        public decimal? ScreenSize { get; set; }
        public string ScreenTech { get; set; } = string.Empty;
        public short? RefreshRate { get; set; }
        public string Chipset { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public short? BatteryCapacity { get; set; }
        public string RearCamera { get; set; } = string.Empty;
        public decimal? Weight { get; set; }
        public string Description { get; set; } = string.Empty;

        public List<VariantEditVM> Variants { get; set; } = new List<VariantEditVM>();

        public List<IFormFile>? GalleryFiles { get; set; } // Nhận ảnh tải lên thêm
        public List<ProductImages> ExistingGallery { get; set; } = new List<ProductImages>(); // Hiển thị ảnh cũ đã lưu
    }

    public class VariantEditVM
    {
        public int VariantId { get; set; }

        [Required]
        public string Color { get; set; } = string.Empty;
        public string Ram { get; set; } = string.Empty;

        [Required]
        public string Storage { get; set; } = string.Empty;

        [Required]
        public decimal Price { get; set; }

        public string? ImageUrl { get; set; }          // Lưu đường dẫn cũ
        public IFormFile? VariantImageFile { get; set; } // Nhận tệp mới nếu muốn thay đổi
        public int? Stock { get; set; }
    }
}