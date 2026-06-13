using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Storefront;

namespace TMDT_LT.Controllers
{
    public class StoreController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StoreController(ApplicationDbContext context)
        {
            _context = context;
        }

        // =======================================================
        // TRANG DANH SÁCH SẢN PHẨM & TÌM KIẾM (UNIFIED CATALOG ENGINE)
        // =======================================================
        [Route("Store")]
        [Route("Store/Index")]
        public async Task<IActionResult> Index(
                    string? keyword,
                    [FromQuery] List<int> brandIds,
                    [FromQuery] List<int> categoryIds,
                    decimal? minPrice,
                    decimal? maxPrice,
                    string sort = "newest",
                    int page = 1) // Biến page mặc định là 1
        {
            // 1. Tải toàn bộ dữ liệu thô hợp lệ lên RAM
            var rawProducts = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.ProductVariants)
                .Where(p => p.IsActive == true)
                .ToListAsync();

            var scoredResults = new List<SearchResultItemVM>();

            // 2. TẦNG LỌC 1: THUẬT TOÁN FUZZY SEARCH KẾT HỢP BIẾN THỂ (SMART ENGINE)
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string kw = keyword.Trim().ToLower();
                string kwNoMark = StringHelper.RemoveDiacritics(kw);

                // Tách từ khóa thành các mảng từ rời để tìm kiếm chéo (VD: "iphone 256gb đỏ")
                var kwParts = kwNoMark.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                scoredResults = rawProducts.Select(p =>
                {
                    int score = 0;

                    // Lấy dữ liệu cơ bản
                    string pName = (p.Name ?? "").ToLower();
                    string pNameNoMark = StringHelper.RemoveDiacritics(pName);
                    string cName = (p.Category?.CategoryName ?? "").ToLower();
                    string cNameNoMark = StringHelper.RemoveDiacritics(cName);
                    string bName = (p.Brand?.BrandName ?? "").ToLower();
                    string bNameNoMark = StringHelper.RemoveDiacritics(bName);
                    string chip = (p.Chipset ?? "").ToLower();
                    string chipNoMark = StringHelper.RemoveDiacritics(chip);

                    // ========================================================
                    // BƯỚC 1: QUÉT THÔNG SỐ TỪ DANH SÁCH BIẾN THỂ (VARIANTS)
                    // ========================================================
                    var activeVariants = p.ProductVariants.Where(v => v.IsActive == true).ToList();

                    // Gom tất cả Màu, RAM, ROM của máy này thành 1 chuỗi dài
                    string variantData = string.Join(" ", activeVariants.Select(v => $"{v.Color} {v.Ram} {v.Storage}")).ToLower();
                    string variantDataNoMark = StringHelper.RemoveDiacritics(variantData);

                    // ========================================================
                    // BƯỚC 2: CHẤM ĐIỂM TÌM KIẾM NGUYÊN CỤM
                    // ========================================================
                    if (pName.Contains(kw)) score += 100;
                    else if (pNameNoMark.Contains(kwNoMark)) score += 70;

                    if (cName.Contains(kw)) score += 60;
                    else if (cNameNoMark.Contains(kwNoMark)) score += 40;

                    if (bName.Contains(kw)) score += 50;
                    else if (bNameNoMark.Contains(kwNoMark)) score += 30;

                    if (chip.Contains(kw)) score += 20;
                    else if (chipNoMark.Contains(kwNoMark)) score += 10;

                    // Điểm cho Màu, RAM, Bộ nhớ nếu gõ chính xác nguyên cụm
                    if (variantData.Contains(kw)) score += 40;
                    else if (variantDataNoMark.Contains(kwNoMark)) score += 20;

                    // ========================================================
                    // BƯỚC 3: CHẤM ĐIỂM CHÉO TỪ KHÓA (CROSS-MATCHING TOKENIZER)
                    // ========================================================
                    // Giải quyết bài toán gõ: "tên máy + thông số" (Vd: "Samsung 512GB")
                    if (kwParts.Length > 1)
                    {
                        // Tạo một "hồ chứa" toàn bộ văn bản của sản phẩm này
                        string fullProductText = $"{pNameNoMark} {cNameNoMark} {bNameNoMark} {chipNoMark} {variantDataNoMark}";
                        int matchCount = 0;

                        // Kiểm tra xem hồ chứa có chứa ĐỦ các từ khóa khách gõ không
                        foreach (var part in kwParts)
                        {
                            if (fullProductText.Contains(part))
                            {
                                matchCount++;
                            }
                        }

                        // Nếu khớp TẤT CẢ các từ (Vd: Vừa có chữ samsung, vừa có chữ 512gb) -> Đẩy lên top
                        if (matchCount == kwParts.Length)
                        {
                            score += 85;
                        }
                        // Nếu khớp một phần, cộng điểm khuyến khích
                        else if (matchCount > 0)
                        {
                            score += (matchCount * 5);
                        }
                    }

                    return new SearchResultItemVM { Product = p, RelevanceScore = score };
                })
                .Where(x => x.RelevanceScore > 0)
                .ToList();
            }
            else
            {
                // Nếu không gõ tìm kiếm, lấy tất cả sản phẩm (Điểm mặc định = 1)
                scoredResults = rawProducts.Select(p => new SearchResultItemVM { Product = p, RelevanceScore = 1 }).ToList();
            }

            // 3. TẦNG LỌC 2: CÁC TIÊU CHÍ CHECKBOX BÊN SIDEBAR
            if (brandIds != null && brandIds.Any())
            {
                scoredResults = scoredResults.Where(x => brandIds.Contains(x.Product.BrandId)).ToList();
            }

            if (categoryIds != null && categoryIds.Any())
            {
                scoredResults = scoredResults.Where(x => categoryIds.Contains(x.Product.CategoryId)).ToList();
            }

            if (minPrice.HasValue)
            {
                scoredResults = scoredResults.Where(x => x.Product.ProductVariants.Any(v => v.IsActive == true && (v.DiscountPrice > 0 ? v.DiscountPrice : v.Price) >= minPrice.Value)).ToList();
            }

            if (maxPrice.HasValue)
            {
                scoredResults = scoredResults.Where(x => x.Product.ProductVariants.Any(v => v.IsActive == true && (v.DiscountPrice > 0 ? v.DiscountPrice : v.Price) <= maxPrice.Value)).ToList();
            }

            // 4. TẦNG LỌC 3: SẮP XẾP KẾT QUẢ
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                // Ưu tiên tuyệt đối sắp xếp theo Điểm trọng số từ cao xuống thấp khi tìm kiếm
                scoredResults = scoredResults.OrderByDescending(x => x.RelevanceScore).ToList();
            }
            else
            {
                switch (sort)
                {
                    case "price_asc":
                        scoredResults = scoredResults.OrderBy(x => x.Product.ProductVariants.Where(v => v.IsActive == true).Min(v => v.DiscountPrice > 0 ? v.DiscountPrice : v.Price)).ToList();
                        break;
                    case "price_desc":
                        scoredResults = scoredResults.OrderByDescending(x => x.Product.ProductVariants.Where(v => v.IsActive == true).Min(v => v.DiscountPrice > 0 ? v.DiscountPrice : v.Price)).ToList();
                        break;
                    case "bestseller-desc":
                        scoredResults = scoredResults.OrderByDescending(x => x.Product.ProductVariants.SelectMany(v => _context.OrderDetails.Where(od => od.VariantId == v.VariantId)).Sum(od => (int?)od.Quantity) ?? 0).ToList();
                        break;
                    default: // newest
                        scoredResults = scoredResults.OrderByDescending(x => x.Product.CreatedDate).ToList();
                        break;
                }
            }

            // 5. TẦNG LỌC 4: THUẬT TOÁN PHÂN TRANG (PAGINATION)
            int pageSize = 12; // Số sản phẩm trên 1 trang (12 chia hết cho 3 và 4, rất đẹp)
            int totalItems = scoredResults.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            // Cắt lấy dữ liệu của trang hiện tại
            var pagedResults = scoredResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // 6. ĐÓNG GÓI DỮ LIỆU ĐƯA RA VIEW GIAO DIỆN
            var vm = new CatalogVM
            {
                Keyword = keyword,
                AvailableBrands = await _context.Brands.ToListAsync(),
                AvailableCategories = await _context.Categories.ToListAsync(),

                Products = pagedResults, // Trả ra dữ liệu ĐÃ BỊ CẮT TRANG
                TotalResults = totalItems, // Trả ra tổng số lượng thực tế

                SelectedBrandIds = brandIds ?? new List<int>(),
                SelectedCategoryIds = categoryIds ?? new List<int>(),
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                SortBy = sort,

                // Gắn dữ liệu phân trang
                CurrentPage = page,
                TotalPages = totalPages,
                PageSize = pageSize
            };

            return View(vm);
        }

        // =======================================================
        // TRANG CHI TIẾT SẢN PHẨM (PDP) & THUẬT TOÁN TIẾP THỊ
        // =======================================================
        [Route("Store/Product/{id}")]
        public async Task<IActionResult> Product(int id)
        {
            // 1. NẠP DỮ LIỆU LÕI: Kéo toàn bộ thông tin máy, biến thể và bộ sưu tập ảnh
            var product = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants)
                .Include(p => p.ProductImages)
                .FirstOrDefaultAsync(p => p.ProductId == id && p.IsActive == true);

            if (product == null) return NotFound();

            // 2. LƯU VẾT HÀNH VI (COOKIE TRACKING)
            // Lưu mã Danh mục/Thương hiệu để trang chủ gợi ý
            Response.Cookies.Append("LastViewedBrandId", product.BrandId.ToString(), new Microsoft.AspNetCore.Http.CookieOptions { Expires = DateTime.Now.AddDays(30) });

            // Lưu lịch sử xem sản phẩm (Tối đa 5 máy gần nhất)
            string recentCookie = Request.Cookies["RecentViews"] ?? "";
            var recentIds = recentCookie.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

            // Xóa ID cũ nếu đã tồn tại để đẩy lên đầu, hoặc giữ nguyên nếu chưa có
            recentIds.Remove(id.ToString());
            recentIds.Insert(0, id.ToString());
            if (recentIds.Count > 5) recentIds = recentIds.Take(5).ToList();

            Response.Cookies.Append("RecentViews", string.Join(",", recentIds), new Microsoft.AspNetCore.Http.CookieOptions { Expires = DateTime.Now.AddDays(7) });

            // 3. THUẬT TOÁN UP-SELLING (BÁN NÂNG CẤP)
            // Tìm các máy cùng danh mục, lấy các máy được tạo mới hơn hoặc có ID khác để gợi ý
            var upSells = await _context.Products
                .Include(p => p.ProductVariants)
                .Where(p => p.CategoryId == product.CategoryId && p.ProductId != id && p.IsActive == true)
                .OrderByDescending(p => p.CreatedDate)
                .Take(5)
                .ToListAsync();

            // 4. THUẬT TOÁN RETARGETING (HIỂN THỊ CÁC MÁY VỪA XEM)
            var recentlyViewedProducts = new List<Products>();
            if (recentIds.Count > 1) // Trừ máy hiện tại ra
            {
                var viewIds = recentIds.Where(x => x != id.ToString()).Select(int.Parse).ToList();
                recentlyViewedProducts = await _context.Products
                    .Include(p => p.ProductVariants)
                    .Where(p => viewIds.Contains(p.ProductId) && p.IsActive == true)
                    .ToListAsync();
            }

            // 5. NẠP DỮ LIỆU ĐÁNH GIÁ (MODULE REVIEWS)
            // Chỉ lấy các bình luận hợp lệ (chưa bị Admin ẩn đi)
            var reviews = await _context.Reviews
                .Include(r => r.Customer) // Nối bảng để lấy Tên khách hàng
                .Where(r => r.ProductId == id && r.IsHidden == false)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            double avgRating = 0;
            int[] starCounts = new int[5]; // Mảng chứa số lượng 1 sao, 2 sao... 5 sao

            if (reviews.Any())
            {
                // Tính điểm trung bình (Làm tròn 1 chữ số thập phân, mặc định 5 nếu null)
                avgRating = Math.Round(reviews.Average(r => r.Rating ?? 5.0), 1);

                // Đếm số lượng cho từng mức sao để vẽ thanh Progress Bar
                starCounts[4] = reviews.Count(r => r.Rating == 5);
                starCounts[3] = reviews.Count(r => r.Rating == 4);
                starCounts[2] = reviews.Count(r => r.Rating == 3);
                starCounts[1] = reviews.Count(r => r.Rating == 2);
                starCounts[0] = reviews.Count(r => r.Rating == 1);
            }

            // Lắp ráp toàn bộ vào ViewModel
            var model = new ProductDetailVM
            {
                Product = product,
                UpSellProducts = upSells,
                //CrossSellProducts = new List<Products>(), // Phần phụ kiện (Cross-sell) phát triển sau
                RecentlyViewed = recentlyViewedProducts,

                // Dữ liệu mới cập nhật cho Module Đánh giá
                ApprovedReviews = reviews,
                AverageRating = avgRating,
                TotalReviews = reviews.Count,
                StarCounts = starCounts
            };

            return View(model);
        }
    }

    // ======================================================================
    // LỚP TIỆN ÍCH (HELPER): XỬ LÝ KÝ TỰ UNICODE TIẾNG VIỆT
    // ======================================================================
    public static class StringHelper
    {
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Xử lý thủ công chữ 'Đ/đ' vì bộ Normalize của C# không thể tách rời ký tự này
            text = text.Replace('đ', 'd').Replace('Đ', 'D');

            // Lột bỏ các dấu thanh (Sắc, huyền, hỏi, ngã, nặng, mũ...)
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}