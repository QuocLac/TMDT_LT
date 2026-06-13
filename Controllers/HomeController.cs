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

            // 4. Khởi tạo danh sách gợi ý cá nhân hóa
            var recommendations = new List<Products>();
            string? favoriteBrandIdStr = Request.Cookies["LastViewedBrandId"];

            if (!string.IsNullOrEmpty(favoriteBrandIdStr) && int.TryParse(favoriteBrandIdStr, out int brandId))
            {
                recommendations = await _context.Products
                    .Include(p => p.ProductVariants)
                    .Where(p => p.IsActive == true && p.BrandId == brandId)
                    .OrderByDescending(p => p.CreatedDate)
                    .Take(10)
                    .ToListAsync();
            }

            if (recommendations.Count < 10)
            {
                var remainingCount = 10 - recommendations.Count;
                var excludedIds = recommendations.Select(r => r.ProductId).ToList();

                var fallbackProducts = await _context.Products
                    .Include(p => p.ProductVariants)
                    .Where(p => p.IsActive == true && !excludedIds.Contains(p.ProductId))
                    .OrderByDescending(p => p.CreatedDate)
                    .Take(remainingCount)
                    .ToListAsync();

                recommendations.AddRange(fallbackProducts);
            }

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

            var model = new HomeStorefrontVM
            {
                ActiveBanners = activeBanners,
                BestSellers = bestSellers,
                Recommendations = recommendations,
                TopVouchers = topVouchers // Đẩy ra View
            };

            return View(model);
        }

        public IActionResult About() => View();
    }
}