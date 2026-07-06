using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Storefront;

namespace TMDT_LT.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. TRANG CHỦ: Tiếp thị sản phẩm thực tế từ Database
        public async Task<IActionResult> Index()
        {
            var now = DateTime.Now;

            // 1. Tải danh sách Banner quảng cáo đang chạy
            var activeBanners = await _context.CampaignBanners
                .Where(b => b.IsActive && b.StartDate <= now && b.EndDate >= now)
                .OrderBy(b => b.DisplayOrder)
                .ThenByDescending(b => b.CreatedAt)
                .ToListAsync();

            // 2. Tải danh mục sản phẩm lên thanh phân mục nhanh
            var categories = await _context.Categories.Take(6).ToListAsync();
            ViewBag.Categories = categories;

            // 3. Tải Sản phẩm bán chạy (BestSellers)
            var bestSellers = await _context.Products
                .Include(p => p.ProductVariants)
                .Where(p => p.IsActive == true)
                .OrderByDescending(p => p.ProductVariants
                    .SelectMany(v => _context.OrderDetails.Where(od => od.VariantId == v.VariantId))
                    .Sum(od => (int?)od.Quantity) ?? 0)
                .Take(10)
                .ToListAsync();

            // 4. Gợi ý cá nhân hóa theo tài khoản/phiên truy cập
            //    - Có đăng nhập: ưu tiên hành vi gắn CustomerId.
            //    - Chưa đăng nhập: fallback theo cookie brand cũ + sản phẩm mới/bán chạy.
            var recommendations = await BuildHomeRecommendationsAsync(12);

            // =============================================================
            // 5. TRUY XUẤT VOUCHER CÔNG KHAI (TargetAudience = 0)
            // =============================================================
            var topVouchers = await _context.Promotions
                .Include(p => p.PromotionRules)
                .Where(p => p.IsActive
                         && p.TargetAudience == 0
                         && p.StartDate <= now
                         && p.EndDate >= now
                         && p.UsedCount < p.UsageLimit)
                .OrderByDescending(p => p.UsedCount) // Lấy mã được dùng nhiều nhất lên trước
                .Take(4)
                .ToListAsync();

            // =============================================================
            // 6. QUÉT SỰ KIỆN FLASH SALE ĐANG HOẠT ĐỘNG
            // =============================================================
            var activeFlashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                    .ThenInclude(i => i.Variant)
                        .ThenInclude(v => v!.Product)
                .Where(f => f.IsActive && f.StartTime <= now && f.EndTime >= now)
                .FirstOrDefaultAsync();

            var model = new HomeStorefrontVM
            {
                ActiveBanners = activeBanners,
                BestSellers = bestSellers,
                Recommendations = recommendations,
                TopVouchers = topVouchers,
                ActiveFlashSale = activeFlashSale // Đẩy dữ liệu ra View
            };

            return View(model);
        }

        private async Task<List<Products>> BuildHomeRecommendationsAsync(int take)
        {
            take = Math.Clamp(take, 6, 16);
            int? customerId = GetCurrentCustomerId();
            var cutoff = DateTime.UtcNow.AddDays(-180);
            var productScores = new Dictionary<int, decimal>();
            var brandScores = new Dictionary<int, decimal>();
            var categoryScores = new Dictionary<int, decimal>();

            void AddScore(int? productId, decimal score)
            {
                if (!productId.HasValue || productId.Value <= 0) return;
                productScores[productId.Value] = productScores.TryGetValue(productId.Value, out var old) ? old + score : score;
            }

            if (customerId.HasValue)
            {
                var events = await _context.AnalyticsEvents
                    .Where(e => e.CustomerId == customerId.Value && e.CreatedAt >= cutoff)
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(500)
                    .Select(e => new { e.EventName, e.ProductId, e.TargetProductId, e.CreatedAt })
                    .ToListAsync();

                foreach (var e in events)
                {
                    AddScore(e.TargetProductId ?? e.ProductId, EventWeight(e.EventName) * RecencyBoost(e.CreatedAt, cutoff));
                }

                var legacyLogs = await _context.UserBehaviorLogs
                    .Where(x => x.CustomerId == customerId.Value && x.CreatedAt >= cutoff)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(250)
                    .Select(x => new { x.ProductId, x.TargetProductId, x.ActionType, x.ViewDuration, x.CreatedAt })
                    .ToListAsync();

                foreach (var log in legacyLogs)
                {
                    decimal score = log.ActionType == "ProductClick" ? 35m : Math.Clamp(log.ViewDuration / 3m, 6m, 30m);
                    AddScore(log.TargetProductId ?? log.ProductId, score * RecencyBoost(log.CreatedAt, cutoff));
                }
            }

            if (productScores.Any())
            {
                var profileProducts = await _context.Products
                    .Where(p => productScores.Keys.Contains(p.ProductId))
                    .Select(p => new { p.ProductId, p.BrandId, p.CategoryId })
                    .ToListAsync();

                foreach (var p in profileProducts)
                {
                    decimal signal = productScores.TryGetValue(p.ProductId, out var s) ? s : 0m;
                    brandScores[p.BrandId] = brandScores.TryGetValue(p.BrandId, out var oldBrand) ? oldBrand + signal * 0.42m : signal * 0.42m;
                    categoryScores[p.CategoryId] = categoryScores.TryGetValue(p.CategoryId, out var oldCategory) ? oldCategory + signal * 0.55m : signal * 0.55m;
                }
            }

            var soldStats = await _context.OrderDetails
                .Where(d => d.Variant != null && d.Variant.ProductId > 0)
                .GroupBy(d => d.Variant!.ProductId)
                .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Quantity ?? 0) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Qty);

            var allProducts = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants)
                .Where(p => p.IsActive == true)
                .ToListAsync();

            var ranked = allProducts
                .Where(p => p.ProductVariants.Any(v => v.IsActive == true))
                .Select(p =>
                {
                    decimal score = 10m;
                    if (productScores.TryGetValue(p.ProductId, out var ps)) score += Math.Min(ps * 0.35m, 70m);
                    if (brandScores.TryGetValue(p.BrandId, out var bs)) score += Math.Min(bs, 95m);
                    if (categoryScores.TryGetValue(p.CategoryId, out var cs)) score += Math.Min(cs, 115m);
                    if (p.ProductVariants.Any(v => v.IsActive == true && (v.Stock ?? 0) > 0)) score += 30m; else score -= 150m;
                    if (p.ProductVariants.Any(v => v.IsActive == true && v.DiscountPrice.HasValue && v.DiscountPrice > 0 && v.Price.HasValue && v.DiscountPrice < v.Price)) score += 18m;
                    if (soldStats.TryGetValue(p.ProductId, out var sold)) score += Math.Min(sold * 2, 60);
                    return new { Product = p, Score = score };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Product.CreatedDate)
                .Take(take)
                .Select(x => x.Product)
                .ToList();

            if (ranked.Count < take)
            {
                string? favoriteBrandIdStr = Request.Cookies["LastViewedBrandId"];
                if (!string.IsNullOrEmpty(favoriteBrandIdStr) && int.TryParse(favoriteBrandIdStr, out int brandId))
                {
                    var excluded = ranked.Select(x => x.ProductId).ToHashSet();
                    var byBrand = allProducts
                        .Where(p => p.BrandId == brandId && !excluded.Contains(p.ProductId))
                        .OrderByDescending(p => p.CreatedDate)
                        .Take(take - ranked.Count)
                        .ToList();
                    ranked.AddRange(byBrand);
                }
            }

            if (ranked.Count < take)
            {
                var excluded = ranked.Select(x => x.ProductId).ToHashSet();
                ranked.AddRange(allProducts
                    .Where(p => !excluded.Contains(p.ProductId))
                    .OrderByDescending(p => soldStats.TryGetValue(p.ProductId, out var sold) ? sold : 0)
                    .ThenByDescending(p => p.CreatedDate)
                    .Take(take - ranked.Count));
            }

            return ranked;
        }

        private int? GetCurrentCustomerId()
        {
            if (User.Identity?.IsAuthenticated != true) return null;
            var value = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(value, out int id) && id > 0 ? id : null;
        }

        private static decimal EventWeight(string? eventName)
        {
            eventName = (eventName ?? string.Empty).Trim().ToLowerInvariant();
            return eventName switch
            {
                "purchase" => 130m,
                "begin_checkout" => 85m,
                "add_to_cart" => 65m,
                "recommendation_click" => 48m,
                "product_click" => 38m,
                "view_item" => 24m,
                "search" => 12m,
                _ => 6m
            };
        }

        private static decimal RecencyBoost(DateTime createdAt, DateTime cutoff)
        {
            var total = Math.Max((DateTime.UtcNow - cutoff).TotalDays, 1);
            var age = Math.Max((DateTime.UtcNow - createdAt).TotalDays, 0);
            var freshness = (decimal)Math.Max(0, 1 - (age / total));
            return 1m + freshness * 1.15m;
        }

        public IActionResult About() => View();
    }
}