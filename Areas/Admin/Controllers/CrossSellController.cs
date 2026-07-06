using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Admin;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CrossSellController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ICrossSellAprioriService _crossSellAprioriService;

        public CrossSellController(ApplicationDbContext context, ICrossSellAprioriService crossSellAprioriService)
        {
            _context = context;
            _crossSellAprioriService = crossSellAprioriService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var settings = await GetOrCreateSettingsAsync();
            var previewRules = await _crossSellAprioriService.GetTopRulePreviewsAsync(30);

            return View(new CrossSellAdminVM
            {
                Settings = settings,
                PreviewRules = previewRules
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(CrossSellAdminVM vm)
        {
            var input = Normalize(vm.Settings ?? new CrossSellSettings());
            var settings = await _context.CrossSellSettings.FirstOrDefaultAsync(x => x.CrossSellSettingsId == 1);

            if (settings == null)
            {
                settings = new CrossSellSettings { CrossSellSettingsId = 1 };
                _context.CrossSellSettings.Add(settings);
            }

            settings.IsEnabled = input.IsEnabled;
            settings.MinSupportCount = input.MinSupportCount;
            settings.MinSupportPercent = input.MinSupportPercent;
            settings.MinConfidence = input.MinConfidence;
            settings.MinLift = input.MinLift;
            settings.MaxItemsetSize = input.MaxItemsetSize;
            settings.MaxRecommendationsPerProduct = input.MaxRecommendationsPerProduct;
            settings.AnalysisWindowDays = input.AnalysisWindowDays;
            settings.OnlyCompletedOrders = input.OnlyCompletedOrders;
            settings.AllowedOrderStatuses = input.AllowedOrderStatuses;
            settings.ExcludeOutOfStock = input.ExcludeOutOfStock;
            settings.AllowFallbackWhenNoRule = input.AllowFallbackWhenNoRule;
            settings.IsBundleDiscountEnabled = input.IsBundleDiscountEnabled;
            settings.BundleDiscountType = input.BundleDiscountType;
            settings.BundleDiscountValue = input.BundleDiscountValue;
            settings.BundleDiscountLabel = input.BundleDiscountLabel;
            settings.UpdatedAt = DateTime.Now;

            var accountIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(accountIdRaw, out int accountId)) settings.UpdatedByAccountId = accountId;

            await _context.SaveChangesAsync();
            TempData["Success"] = "Đã lưu cấu hình gợi ý mua kèm.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<CrossSellSettings> GetOrCreateSettingsAsync()
        {
            var settings = await _context.CrossSellSettings.FirstOrDefaultAsync(x => x.CrossSellSettingsId == 1);
            if (settings != null) return Normalize(settings);

            settings = new CrossSellSettings { CrossSellSettingsId = 1, UpdatedAt = DateTime.Now };
            _context.CrossSellSettings.Add(settings);
            await _context.SaveChangesAsync();
            return settings;
        }

        private static CrossSellSettings Normalize(CrossSellSettings settings)
        {
            settings.MinSupportCount = Math.Max(1, settings.MinSupportCount);
            settings.MinSupportPercent = Math.Clamp(settings.MinSupportPercent, 0m, 1m);
            settings.MinConfidence = Math.Clamp(settings.MinConfidence, 0m, 1m);
            settings.MinLift = Math.Clamp(settings.MinLift, 0m, 100m);
            settings.MaxItemsetSize = Math.Clamp(settings.MaxItemsetSize, 2, 4);
            settings.MaxRecommendationsPerProduct = Math.Clamp(settings.MaxRecommendationsPerProduct, 1, 24);
            settings.AnalysisWindowDays = Math.Clamp(settings.AnalysisWindowDays, 0, 3650);
            settings.BundleDiscountType = settings.BundleDiscountType == 1 ? 1 : 0;
            settings.BundleDiscountValue = Math.Clamp(settings.BundleDiscountValue, 0m, 100000000m);
            settings.BundleDiscountLabel = string.IsNullOrWhiteSpace(settings.BundleDiscountLabel)
                ? "Ưu đãi mua kèm"
                : settings.BundleDiscountLabel.Trim();
            settings.AllowedOrderStatuses = string.IsNullOrWhiteSpace(settings.AllowedOrderStatuses)
                ? "Đã hoàn thành,Hoàn thành,Đã giao,Completed"
                : settings.AllowedOrderStatuses.Trim();
            return settings;
        }
    }
}
