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
    // Cấu trúc nhận dữ liệu Tracking ngầm từ Client gửi lên
    public class UserBehaviorTrackingDto
    {
        public int ProductId { get; set; }
        public int? TargetProductId { get; set; }
        public int ViewDuration { get; set; }
        public string? SearchKeyword { get; set; }
        public string ActionType { get; set; } = "ViewDuration";
    }

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
                    int page = 1)
        {
            int currentCustomerId = 0;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                int.TryParse(userIdStr, out currentCustomerId);
            }

            List<int> preferredCategoryIds = new List<int>();
            List<int> preferredBrandIds = new List<int>();

            if (currentCustomerId > 0)
            {
                var userInteractedProductIds = await _context.UserBehaviorLogs
                    .Where(log => log.CustomerId == currentCustomerId && (log.ViewDuration > 5 || log.ActionType == "ProductClick"))
                    .Select(log => log.ProductId)
                    .Distinct()
                    .Take(50)
                    .ToListAsync();

                if (userInteractedProductIds.Any())
                {
                    var productDetails = await _context.Products
                        .Where(p => userInteractedProductIds.Contains(p.ProductId))
                        .Select(p => new { p.CategoryId, p.BrandId })
                        .ToListAsync();

                    preferredCategoryIds = productDetails.Select(x => x.CategoryId).Distinct().ToList();
                    preferredBrandIds = productDetails.Select(x => x.BrandId).Distinct().ToList();
                }
            }

            var rawProducts = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.ProductVariants)
                .Where(p => p.IsActive == true)
                .ToListAsync();

            var scoredResults = new List<SearchResultItemVM>();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string kw = keyword.Trim().ToLower();
                string kwNoMark = StringHelper.RemoveDiacritics(kw);

                var kwParts = kwNoMark.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                scoredResults = rawProducts.Select(p =>
                {
                    int score = 0;

                    string pName = (p.Name ?? "").ToLower();
                    string pNameNoMark = StringHelper.RemoveDiacritics(pName);
                    string cName = (p.Category?.CategoryName ?? "").ToLower();
                    string cNameNoMark = StringHelper.RemoveDiacritics(cName);
                    string bName = (p.Brand?.BrandName ?? "").ToLower();
                    string bNameNoMark = StringHelper.RemoveDiacritics(bName);
                    string chip = (p.Chipset ?? "").ToLower();
                    string chipNoMark = StringHelper.RemoveDiacritics(chip);

                    var activeVariants = p.ProductVariants.Where(v => v.IsActive == true).ToList();
                    string variantData = string.Join(" ", activeVariants.Select(v => $"{v.Color} {v.Ram} {v.Storage}")).ToLower();
                    string variantDataNoMark = StringHelper.RemoveDiacritics(variantData);

                    if (pName.Contains(kw)) score += 100;
                    else if (pNameNoMark.Contains(kwNoMark)) score += 70;

                    if (cName.Contains(kw)) score += 60;
                    else if (cNameNoMark.Contains(kwNoMark)) score += 40;

                    if (bName.Contains(kw)) score += 50;
                    else if (bNameNoMark.Contains(kwNoMark)) score += 30;

                    if (chip.Contains(kw)) score += 20;
                    else if (chipNoMark.Contains(kwNoMark)) score += 10;

                    if (variantData.Contains(kw)) score += 40;
                    else if (variantDataNoMark.Contains(kwNoMark)) score += 20;

                    if (kwParts.Length > 1)
                    {
                        string fullProductText = $"{pNameNoMark} {cNameNoMark} {bNameNoMark} {chipNoMark} {variantDataNoMark}";
                        int matchCount = 0;

                        foreach (var part in kwParts)
                        {
                            if (fullProductText.Contains(part)) matchCount++;
                        }

                        if (matchCount == kwParts.Length) score += 85;
                        else if (matchCount > 0) score += (matchCount * 5);
                    }

                    if (preferredCategoryIds.Contains(p.CategoryId)) score += 15;
                    if (preferredBrandIds.Contains(p.BrandId)) score += 15;

                    return new SearchResultItemVM { Product = p, RelevanceScore = score };
                })
                .Where(x => x.RelevanceScore > 0)
                .ToList();
            }
            else
            {
                scoredResults = rawProducts.Select(p => {
                    int score = 1;
                    if (preferredCategoryIds.Contains(p.CategoryId)) score += 10;
                    if (preferredBrandIds.Contains(p.BrandId)) score += 10;
                    return new SearchResultItemVM { Product = p, RelevanceScore = score };
                }).ToList();
            }

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

                    var minVariantPrice = activeVariants.Min(v => v.DiscountPrice > 0 ? v.DiscountPrice : v.Price);
                    bool matchMin = !minPrice.HasValue || minVariantPrice >= minPrice.Value;
                    bool matchMax = !maxPrice.HasValue || minVariantPrice <= maxPrice.Value;

                    return matchMin && matchMax;
                }).ToList();
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
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
                    default:
                        scoredResults = scoredResults.OrderByDescending(x => x.RelevanceScore).ThenByDescending(x => x.Product.CreatedDate).ToList();
                        break;
                }
            }

            int pageSize = 12;
            int totalItems = scoredResults.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var pagedResults = scoredResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var dbBrands = await _context.Brands.Select(b => b.BrandName).Take(5).ToListAsync();
            var dbCategories = await _context.Categories.Where(c => c.IsActive == true).Select(c => c.CategoryName).Take(4).ToListAsync();

            var popularSearches = await _context.UserBehaviorLogs
                .Where(log => !string.IsNullOrEmpty(log.SearchKeyword))
                .GroupBy(log => log.SearchKeyword)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(3)
                .ToListAsync();

            var tags = new List<string>();
            if (popularSearches.Any()) tags.AddRange(popularSearches!);
            tags.AddRange(dbBrands);
            tags.AddRange(dbCategories);

            if (!tags.Any())
            {
                tags = new List<string> { "iPhone", "Samsung", "Oppo", "Laptop", "iPad" };
            }

            var vm = new CatalogVM
            {
                Keyword = keyword,
                AvailableBrands = await _context.Brands.ToListAsync(),
                AvailableCategories = await _context.Categories.ToListAsync(),
                Products = pagedResults,
                TotalResults = totalItems,
                SelectedBrandIds = brandIds ?? new List<int>(),
                SelectedCategoryIds = categoryIds ?? new List<int>(),
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                SortBy = sort,
                CurrentPage = page,
                TotalPages = totalPages,
                PageSize = pageSize,
                SuggestionTags = tags.Distinct().ToList()
            };

            return View(vm);
        }

        // =====================================================================
        // API LẤY CỤM TỪ GỢI Ý DANH SÁCH DỌC ĐÚNG KIỂU SHOPEE (IMAGE_ED099A.PNG)
        // =====================================================================
       [HttpGet]
[Route("Store/GetSearchSuggestions")]
public async Task<IActionResult> GetSearchSuggestions(string q)
{
    var suggestedPhrases = new List<string>();

    // Trường hợp 1: Ô tìm kiếm trống -> gợi ý từ khóa hot hoặc danh mục nổi bật
    if (string.IsNullOrWhiteSpace(q))
    {
        // Lấy 7 từ khóa hot từ UserBehaviorLogs
        var hotKeywords = await _context.UserBehaviorLogs
            .Where(log => !string.IsNullOrEmpty(log.SearchKeyword))
            .GroupBy(log => log.SearchKeyword)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(7)
            .ToListAsync();

        // Nếu chưa có lịch sử, dùng danh sách gợi ý mặc định
        if (hotKeywords == null || !hotKeywords.Any())
        {
            hotKeywords = new List<string> { "iPhone", "Samsung", "Oppo", "Xiaomi", "Laptop", "Tai nghe", "Phụ kiện" };
        }

        return Json(new { isBlank = true, phrases = hotKeywords });
    }

    // Trường hợp 2: Có từ khóa nhập vào
    string query = q.Trim().ToLower();
    string queryNoMark = StringHelper.RemoveDiacritics(query);

    // 1. Lấy danh mục phù hợp
    var matchedCategories = await _context.Categories
        .Where(c => c.IsActive == true)
        .Select(c => c.CategoryName)
        .ToListAsync();
    suggestedPhrases.AddRange(matchedCategories
        .Where(c => c.ToLower().Contains(query) || StringHelper.RemoveDiacritics(c.ToLower()).Contains(queryNoMark))
        .Take(2));

    // 2. Lấy thương hiệu phù hợp
    var matchedBrands = await _context.Brands
        .Select(b => b.BrandName)
        .ToListAsync();
    suggestedPhrases.AddRange(matchedBrands
        .Where(b => b.ToLower().Contains(query) || StringHelper.RemoveDiacritics(b.ToLower()).Contains(queryNoMark))
        .Take(2));

    // 3. Lấy tên sản phẩm phù hợp (cắt ngắn để dễ nhìn)
    var matchedProducts = await _context.Products
        .Where(p => p.IsActive == true)
        .Select(p => p.Name)
        .ToListAsync();
    suggestedPhrases.AddRange(matchedProducts
        .Where(n => n.ToLower().Contains(query) || StringHelper.RemoveDiacritics(n.ToLower()).Contains(queryNoMark))
        .Take(5));

    // 4. Lấy lịch sử tìm kiếm phù hợp
    var matchedHistory = await _context.UserBehaviorLogs
        .Where(log => !string.IsNullOrEmpty(log.SearchKeyword))
        .Select(log => log.SearchKeyword)
        .Distinct()
        .ToListAsync();
    suggestedPhrases.AddRange(matchedHistory!
        .Where(h => h!.ToLower().Contains(query) || StringHelper.RemoveDiacritics(h.ToLower()).Contains(queryNoMark))
        .Take(2));

    // Loại bỏ trùng và giới hạn 8 kết quả
    var finalResult = suggestedPhrases
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(8)
        .ToList();

    return Json(new { isBlank = false, phrases = finalResult });
}

        [HttpPost]
        [Route("Store/TrackUserBehavior")]
        public async Task<IActionResult> TrackUserBehavior([FromBody] UserBehaviorTrackingDto dto)
        {
            if (dto == null) return BadRequest();

            int? customerId = null;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                if (int.TryParse(userIdStr, out int id) && id > 0)
                {
                    customerId = id;
                }
            }

            var logEntry = new UserBehaviorLog
            {
                CustomerId = customerId,
                ProductId = dto.ProductId,
                TargetProductId = dto.TargetProductId,
                ViewDuration = dto.ViewDuration,
                SearchKeyword = string.IsNullOrWhiteSpace(dto.SearchKeyword) ? null : dto.SearchKeyword.Trim(),
                ActionType = dto.ActionType,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserBehaviorLogs.Add(logEntry);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

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

            var upSellProducts = await _context.Products
                .Include(p => p.ProductVariants.Where(v => v.IsActive == true))
                .Where(p => p.CategoryId == product.CategoryId && p.ProductId != id && p.IsActive == true)
                .Take(10)
                .ToListAsync();

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

            var model = new ProductDetailVM
            {
                Product = product,
                UpSellProducts = upSellProducts,
                ApprovedReviews = displayReviews,
                AverageRating = avgRating,
                TotalReviews = allReviews.Count,
                StarCounts = starCounts
            };

            bool hasPurchased = false;
            if (User.Identity != null && User.Identity.IsAuthenticated)
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

        [Route("Store/Product/{id}/Reviews")]
        public async Task<IActionResult> ProductReviews(int id, int? starFilter)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == id);
            if (product == null) return NotFound();

            var query = _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.ReviewDetails).ThenInclude(rd => rd.Variant)
                .Where(r => r.ProductId == id && r.IsHidden == false);

            var allReviews = await query.ToListAsync();

            double avgRating = allReviews.Any() ? (double)allReviews.Average(r => r.Rating ?? 0) : 0;
            var starCounts = new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } };

            foreach (var r in allReviews)
            {
                int star = r.Rating ?? 5;
                if (starCounts.ContainsKey(star)) starCounts[star]++;
            }

            if (starFilter.HasValue && starFilter.Value >= 1 && starFilter.Value <= 5)
            {
                query = query.Where(r => r.Rating == starFilter.Value);
            }

            var displayReviews = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

            ViewBag.Product = product;
            ViewBag.AverageRating = avgRating;
            ViewBag.TotalReviews = allReviews.Count;
            ViewBag.StarCounts = starCounts;
            ViewBag.SelectedStar = starFilter;

            return View(displayReviews);
        }
    }

    public static class StringHelper
    {
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Replace('đ', 'd').Replace('Đ', 'D');

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