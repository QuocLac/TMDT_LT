using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels
{
    public class ProductCreateVM
    {
        [Required(ErrorMessage = "Tên sản phẩm không được để trống")]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int CategoryId { get; set; }

        [Required]
        public int BrandId { get; set; }

        [Required]
        public string MainImage { get; set; } = string.Empty;

        // Thông số kỹ thuật cơ bản
        public decimal? ScreenSize { get; set; }
        public string ScreenTech { get; set; } = string.Empty;
        public short? RefreshRate { get; set; }
        public string Chipset { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public short? BatteryCapacity { get; set; }
        public string RearCamera { get; set; } = string.Empty;
        public decimal? Weight { get; set; }

        public string Description { get; set; } = string.Empty;

        // Danh sách các biến thể sẽ tạo cùng lúc
        public List<VariantCreateVM> Variants { get; set; } = new List<VariantCreateVM>();
    }

    public class VariantCreateVM
    {
        [Required]
        public string Color { get; set; } = string.Empty;
        [Required]
        public string RAM { get; set; } = string.Empty;
        [Required]
        public string Storage { get; set; } = string.Empty;
        [Required]
        public decimal Price { get; set; }

        public string? ImageUrl { get; set; }
    }
}