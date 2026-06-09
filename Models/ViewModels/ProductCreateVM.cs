using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels
{
    public class ProductCreateVM
    {
        [Required(ErrorMessage = "Tên sản phẩm không được để trống")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng chọn danh mục")]
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn thương hiệu")]
        public int BrandId { get; set; }

        // Nhận tệp tin hình ảnh chính từ máy tính
        [Required(ErrorMessage = "Vui lòng chọn hình ảnh đại diện")]
        public IFormFile? MainImageFile { get; set; }

        public decimal? ScreenSize { get; set; }
        public string ScreenTech { get; set; } = string.Empty;
        public short? RefreshRate { get; set; }
        public string Chipset { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public short? BatteryCapacity { get; set; }
        public string RearCamera { get; set; } = string.Empty;
        public decimal? Weight { get; set; }
        public string Description { get; set; } = string.Empty;

        public List<VariantCreateVM> Variants { get; set; } = new List<VariantCreateVM>();

        public List<IFormFile>? GalleryFiles { get; set; }
    }

    public class VariantCreateVM
    {
        [Required(ErrorMessage = "Màu sắc không được trống")]
        public string Color { get; set; } = string.Empty;
        public string Ram { get; set; } = string.Empty;

        [Required(ErrorMessage = "Dung lượng bộ nhớ không được trống")]
        public string Storage { get; set; } = string.Empty;

        [Required(ErrorMessage = "Giá bán không được trống")]
        public decimal Price { get; set; }

        // Nhận tệp tin hình ảnh riêng của biến thể (Không bắt buộc)
        public IFormFile? VariantImageFile { get; set; }
    }
}