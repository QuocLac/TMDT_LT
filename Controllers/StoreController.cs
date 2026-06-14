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
using TMDT_LT.Models.ViewModels;

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

            if (minPrice.HasValue || maxPrice.HasValue)
            {
                scoredResults = scoredResults.Where(x =>
                {
                    var activeVariants = x.Product.ProductVariants.Where(v => v.IsActive == true);
                    if (!activeVariants.Any()) return false;

                    // Tìm giá biến thể rẻ nhất (chính là giá hiển thị trên card)
                    var minVariantPrice = activeVariants.Min(v => v.DiscountPrice > 0 ? v.DiscountPrice : v.Price);

                    bool matchMin = !minPrice.HasValue || minVariantPrice >= minPrice.Value;
                    bool matchMax = !maxPrice.HasValue || minVariantPrice <= maxPrice.Value;

                    return matchMin && matchMax;
                }).ToList();
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

        // =====================================================================
        // 1. CHI TIẾT SẢN PHẨM (PDP) - ĐÃ SỬA LỖI KHÔNG HIỂN THỊ SẢN PHẨM LIÊN QUAN
        // =====================================================================
        [Route("Store/Product/{id}")]
        public async Task<IActionResult> Product(int id)
        {
            var product = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants.Where(v => v.IsActive == true))
                .Include(p => p.ProductImages)
                .FirstOrDefaultAsync(p => p.ProductId == id && p.IsActive == true);

            if (product == null) return RedirectToAction("Index");

            // --- THUẬT TOÁN GỢI Ý SẢN PHẨM LIÊN QUAN CHUẨN DOANH NGHIỆP ---
            var upSellProducts = await _context.Products
                .Include(p => p.ProductVariants.Where(v => v.IsActive == true))
                .Where(p => p.CategoryId == product.CategoryId && p.ProductId != id && p.IsActive == true)
                .Take(10)
                .ToListAsync();

            // Lấy tất cả đánh giá công khai phục vụ thống kê số sao
            var allReviews = await _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.ReviewDetails).ThenInclude(rd => rd.Variant)
                .Where(r => r.ProductId == id && r.IsHidden == false)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            double avgRating = allReviews.Any() ? (double)allReviews.Average(r => r.Rating ?? 0) : 0;
            var starCounts = new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } };
            foreach (var r in allReviews)
            {
                int star = r.Rating ?? 5;
                if (starCounts.ContainsKey(star)) starCounts[star]++;
            }

            var displayReviews = allReviews.Take(3).ToList();

            // ĐÓNG GÓI VIEW MODEL TOÀN DIỆN DỮ LIỆU
            var model = new ProductDetailVM
            {
                Product = product,
                UpSellProducts = upSellProducts, // FIX CHÍ MẠNG: Đổ dữ liệu thuật toán vào đây để không bị mất giao diện
                ApprovedReviews = displayReviews,
                AverageRating = avgRating,
                TotalReviews = allReviews.Count,
                StarCounts = starCounts
            };

            // Ràng buộc kiểm tra quyền viết đánh giá của khách
            bool hasPurchased = false;
            if (User.Identity.IsAuthenticated)
            {
                string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                int customerId = int.Parse(userIdStr);

                hasPurchased = await _context.Orders
                    .Include(o => o.OrderDetails)
                    .AnyAsync(o => o.CustomerId == customerId && o.Status == "Đã hoàn thành" && o.OrderDetails.Any(d => d.Variant.ProductId == id));
            }
            ViewBag.HasPurchased = hasPurchased;

            return View(model);
        }

        // =====================================================================
        // TRANG CHI TIẾT DANH SÁCH ĐÁNH GIÁ SẢN PHẨM (REVIEWS PAGE)
        // =====================================================================
        [Route("Store/Product/{id}/Reviews")]
        public async Task<IActionResult> ProductReviews(int id, int? starFilter)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == id);
            if (product == null) return NotFound();

            // Lấy toàn bộ đánh giá công khai của sản phẩm này
            var query = _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.ReviewDetails).ThenInclude(rd => rd.Variant)
                .Where(r => r.ProductId == id && r.IsHidden == false);

            var allReviews = await query.ToListAsync();

            // Tính toán tổng quan (Số sao trung bình, phân bổ phần trăm)
            double avgRating = allReviews.Any() ? (double)allReviews.Average(r => r.Rating ?? 0) : 0;
            var starCounts = new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } };

            foreach (var r in allReviews)
            {
                int star = r.Rating ?? 5;
                if (starCounts.ContainsKey(star)) starCounts[star]++;
            }

            // Nếu người dùng click vào nút lọc theo số sao
            if (starFilter.HasValue && starFilter.Value >= 1 && starFilter.Value <= 5)
            {
                query = query.Where(r => r.Rating == starFilter.Value);
            }

            // Lấy dữ liệu cuối cùng đưa ra View
            var displayReviews = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

            ViewBag.Product = product;
            ViewBag.AverageRating = avgRating;
            ViewBag.TotalReviews = allReviews.Count;
            ViewBag.StarCounts = starCounts;
            ViewBag.SelectedStar = starFilter;

            return View(displayReviews); // Đổ dữ liệu vào file ProductReviews.cshtml
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