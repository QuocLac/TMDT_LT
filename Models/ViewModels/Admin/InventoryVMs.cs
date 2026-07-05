using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models.ViewModels.Admin
{
    // DTO phục vụ cho tìm kiếm Variant kèm thông tin giá nhập thực tế
    public class VariantInventorySearchResult
    {
        public int VariantId { get; set; }
        public string ProductCode { get; set; } = null!;
        public string ProductName { get; set; } = null!;
        public string VariantName { get; set; } = null!;
        public decimal ReferencePrice { get; set; } // Giá bán lẻ hiện tại
        public decimal LastImportPrice { get; set; } // Giá nhập kho gần nhất (Lấy từ DB)
        public int CurrentStock { get; set; }
    }

    // DTO Nhập kho (Purchase Order)
    public class CreatePORequest
    {
        [Required]
        public int SupplierId { get; set; }

        [Required]
        public int BranchId { get; set; }

        public string? Notes { get; set; }

        [Range(0, double.MaxValue)]
        public decimal ShippingFee { get; set; }

        [Required]
        public List<PODetailItem> Items { get; set; } = new();
    }

    public class PODetailItem
    {
        [Required]
        public int VariantId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng nhập phải lớn hơn 0")]
        public int Quantity { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Giá nhập không hợp lệ")]
        public decimal ImportPrice { get; set; }

        [Range(0, 100, ErrorMessage = "Thuế suất từ 0 đến 100%")]
        public decimal TaxRate { get; set; } // Thuế suất cho từng dòng sản phẩm
    }

    // DTO Xuất kho / Phân phối (Sales Order)
    public class CreateSORequest
    {
        [Required]
        public int DestinationBranchId { get; set; } // Chi nhánh nhận hàng

        public string? Notes { get; set; }

        [Required]
        public List<SODetailItem> Items { get; set; } = new();
    }

    public class SODetailItem
    {
        [Required]
        public int VariantId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng xuất phải lớn hơn 0")]
        public int Quantity { get; set; }
    }
}