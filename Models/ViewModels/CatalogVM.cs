using System.Collections.Generic;
using TMDT_LT.Models;

namespace TMDT_LT.Models.ViewModels.Storefront
{
    public class CatalogVM
    {
        public string? Keyword { get; set; }
        public List<Categories> AvailableCategories { get; set; } = new List<Categories>();
        public List<Brands> AvailableBrands { get; set; } = new List<Brands>();
        public List<int> SelectedBrandIds { get; set; } = new List<int>();
        public List<int> SelectedCategoryIds { get; set; } = new List<int>();
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public string SortBy { get; set; } = "newest";
        public List<SearchResultItemVM> Products { get; set; } = new List<SearchResultItemVM>();

        // Tổng số kết quả tìm được trên toàn hệ thống (không phải số lượng của 1 trang)
        public int TotalResults { get; set; }

        // CÁC THUỘC TÍNH PHÂN TRANG MỚI THÊM:
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; }
        public int PageSize { get; set; } = 12; // Mặc định hiển thị 12 sản phẩm/trang (hợp với lưới 3 cột)
    }
}