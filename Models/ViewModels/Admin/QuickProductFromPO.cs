using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels.Admin
{
    public class QuickProductFromPO
    {
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
        [StringLength(100)]
        [Display(Name = "Tên sản phẩm")]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Danh mục là bắt buộc")]
        [Display(Name = "Danh mục")]
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Thương hiệu là bắt buộc")]
        [Display(Name = "Thương hiệu")]
        public int BrandId { get; set; }

        [Required(ErrorMessage = "Màu sắc là bắt buộc")]
        [StringLength(30)]
        [Display(Name = "Màu sắc")]
        public string Color { get; set; } = null!;

        [Required(ErrorMessage = "Dung lượng lưu trữ là bắt buộc")]
        [StringLength(20)]
        [Display(Name = "Dung lượng lưu trữ")]
        public string Storage { get; set; } = null!;

        [StringLength(20)]
        [Display(Name = "RAM")]
        public string? Ram { get; set; }

        [StringLength(50)]
        [Display(Name = "Chipset")]
        public string? Chipset { get; set; }

        [StringLength(30)]
        [Display(Name = "Hệ điều hành")]
        public string? OperatingSystem { get; set; }

        [Required(ErrorMessage = "Giá nhập là bắt buộc")]
        [Range(1000, double.MaxValue, ErrorMessage = "Giá nhập phải lớn hơn 0")]
        [Display(Name = "Giá nhập (Giá vốn)")]
        public decimal ImportPrice { get; set; }

        [Required(ErrorMessage = "Giá bán là bắt buộc")]
        [Range(1000, double.MaxValue, ErrorMessage = "Giá bán phải lớn hơn 0")]
        [Display(Name = "Giá bán niêm yết")]
        public decimal ListPrice { get; set; }

        [Required(ErrorMessage = "Số lượng nhập là bắt buộc")]
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng nhập ít nhất là 1")]
        [Display(Name = "Số lượng nhập")]
        public int Quantity { get; set; }

        [Display(Name = "Mô tả sản phẩm")]
        public string? Description { get; set; }

        [StringLength(50)]
        [Display(Name = "Kích thước")]
        public string? Dimensions { get; set; }

        [Display(Name = "Hình ảnh sản phẩm")]
        public IFormFile? ImageFile { get; set; }

        [Display(Name = "Đăng bán ngay")]
        public bool IsPublished { get; set; } = true;
    }
}