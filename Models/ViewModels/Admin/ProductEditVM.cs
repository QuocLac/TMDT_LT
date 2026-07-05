using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels.Admin
{
    public class ProductEditVM
    {
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
        [StringLength(100, ErrorMessage = "Tên sản phẩm tối đa 100 ký tự")]
        [Display(Name = "Tên sản phẩm")]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Danh mục là bắt buộc")]
        [Display(Name = "Danh mục")]
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Thương hiệu là bắt buộc")]
        [Display(Name = "Thương hiệu")]
        public int BrandId { get; set; }

        [Display(Name = "Hình ảnh chính")]
        public IFormFile? MainImageFile { get; set; }

        [Display(Name = "Hình ảnh hiện tại")]
        public string? MainImage { get; set; }

        // --- THÔNG SỐ KỸ THUẬT ---
        [Display(Name = "Chipset / Vi xử lý")]
        [StringLength(50)]
        public string? Chipset { get; set; }

        [Display(Name = "Hệ điều hành")]
        [StringLength(30)]
        public string? OperatingSystem { get; set; }

        [Display(Name = "Dung lượng pin (mAh)")]
        public short? BatteryCapacity { get; set; }

        [Display(Name = "Kích thước màn hình (inch)")]
        public decimal? ScreenSize { get; set; }

        [Display(Name = "Công nghệ màn hình")]
        [StringLength(40)]
        public string? ScreenTech { get; set; }

        [Display(Name = "Tần số quét (Hz)")]
        public short? RefreshRate { get; set; }

        [Display(Name = "Camera sau")]
        [StringLength(100)]
        public string? RearCamera { get; set; }

        // 🔥 THÊM MỚI - Camera trước
        [Display(Name = "Camera trước")]
        [StringLength(50)]
        public string? FrontCamera { get; set; }

        [Display(Name = "Trọng lượng (g)")]
        public decimal? Weight { get; set; }

        // 🔥 THÊM MỚI - Kích thước
        [Display(Name = "Kích thước")]
        [StringLength(50)]
        public string? Dimensions { get; set; }

        [Display(Name = "Mô tả sản phẩm")]
        public string? Description { get; set; }

        [Display(Name = "Ngày phát hành")]
        public DateTime? ReleaseDate { get; set; }

        // --- DANH SÁCH BIẾN THỂ HIỆN TẠI ---
        [Display(Name = "Danh sách biến thể")]
        public List<VariantEditVM> Variants { get; set; } = new List<VariantEditVM>();

        // --- THƯ VIỆN HÌNH ẢNH ---
        [Display(Name = "Thư viện ảnh hiện tại")]
        public List<ProductImages>? ExistingGallery { get; set; }

        [Display(Name = "Thêm ảnh mới vào thư viện")]
        public List<IFormFile>? GalleryFiles { get; set; }
    }

    public class VariantEditVM
    {
        public int VariantId { get; set; }

        [Required(ErrorMessage = "Màu sắc là bắt buộc")]
        [StringLength(30)]
        [Display(Name = "Màu sắc")]
        public string Color { get; set; } = null!;

        [StringLength(20)]
        [Display(Name = "Bộ nhớ RAM")]
        public string? Ram { get; set; }

        [Required(ErrorMessage = "Dung lượng lưu trữ là bắt buộc")]
        [StringLength(20)]
        [Display(Name = "Dung lượng lưu trữ")]
        public string Storage { get; set; } = null!;

        [Required(ErrorMessage = "Giá bán là bắt buộc")]
        [Range(1000, double.MaxValue, ErrorMessage = "Giá bán phải lớn hơn 0")]
        [Display(Name = "Giá bán niêm yết")]
        public decimal Price { get; set; }

        // 🔥 THÊM MỚI - Giá khuyến mãi
        [Display(Name = "Giá khuyến mãi")]
        public decimal? DiscountPrice { get; set; }

        [Display(Name = "Hình ảnh hiện tại")]
        public string? ImageUrl { get; set; }

        [Display(Name = "Cập nhật hình ảnh")]
        public IFormFile? VariantImageFile { get; set; }

        // 🔥 THÊM MỚI - Số lượng tồn kho
        [Display(Name = "Số lượng tồn kho")]
        public int? Stock { get; set; }
    }
}