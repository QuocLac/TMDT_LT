using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AnalyticsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly GoogleAnalyticsService _googleAnalyticsService;

        public AnalyticsController(ApplicationDbContext context, GoogleAnalyticsService googleAnalyticsService)
        {
            _context = context;
            _googleAnalyticsService = googleAnalyticsService;
        }

        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate)
        {
            var to = (toDate ?? DateTime.Today).Date.AddDays(1).AddTicks(-1);
            var from = (fromDate ?? DateTime.Today.AddDays(-13)).Date;

            ViewBag.GoogleAnalyticsStatus = await _googleAnalyticsService.CheckConnectionAsync();

            var sessions = await _context.AnalyticsSessions
                .Where(s => s.FirstSeenAt >= from && s.FirstSeenAt <= to)
                .ToListAsync();

            var eventsQuery = _context.AnalyticsEvents.Where(e => e.CreatedAt >= from && e.CreatedAt <= to);
            var events = await eventsQuery.ToListAsync();

            int sessionCount = sessions.Count;
            int pageViews = events.Count(e => e.EventName == "page_view_internal" || e.EventName == "page_view");
            int productViews = events.Count(e => e.EventName == "view_item");
            int addToCartSessions = sessions.Count(s => s.HasAddToCart);
            int checkoutSessions = sessions.Count(s => s.HasCheckout);
            int purchaseSessions = sessions.Count(s => s.HasPurchase);
            decimal revenue = sessions.Sum(s => s.TotalRevenue);

            ViewBag.FromDate = from.ToString("yyyy-MM-dd");
            ViewBag.ToDate = to.Date.ToString("yyyy-MM-dd");
            ViewBag.Sessions = sessionCount;
            ViewBag.PageViews = pageViews;
            ViewBag.ProductViews = productViews;
            ViewBag.AddToCartSessions = addToCartSessions;
            ViewBag.CheckoutSessions = checkoutSessions;
            ViewBag.PurchaseSessions = purchaseSessions;
            ViewBag.Revenue = revenue;
            ViewBag.AddToCartRate = Rate(addToCartSessions, sessionCount);
            ViewBag.CheckoutRate = Rate(checkoutSessions, sessionCount);
            ViewBag.PurchaseRate = Rate(purchaseSessions, sessionCount);

            var daily = events
                .GroupBy(e => e.CreatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Date = g.Key.ToString("dd/MM"),
                    PageViews = g.Count(e => e.EventName == "page_view_internal" || e.EventName == "page_view"),
                    ProductViews = g.Count(e => e.EventName == "view_item"),
                    AddToCart = g.Count(e => e.EventName == "add_to_cart"),
                    Checkout = g.Count(e => e.EventName == "begin_checkout"),
                    Purchase = g.Count(e => e.EventName == "purchase")
                })
                .ToList();

            ViewBag.Daily = daily;

            var topProducts = await eventsQuery
                .Where(e => e.ProductId.HasValue)
                .GroupBy(e => e.ProductId!.Value)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Views = g.Count(e => e.EventName == "view_item"),
                    AddToCart = g.Count(e => e.EventName == "add_to_cart"),
                    Purchases = g.Count(e => e.EventName == "purchase")
                })
                .OrderByDescending(x => x.Views + x.AddToCart * 3 + x.Purchases * 10)
                .Take(10)
                .ToListAsync();

            var productIds = topProducts.Select(x => x.ProductId).ToList();
            var products = await _context.Products
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.Name })
                .ToListAsync();

            ViewBag.TopProducts = topProducts.Select(x => new
            {
                Name = products.FirstOrDefault(p => p.ProductId == x.ProductId)?.Name ?? $"SP #{x.ProductId}",
                x.Views,
                x.AddToCart,
                x.Purchases
            }).ToList();

            ViewBag.TopSearches = await eventsQuery
                .Where(e => !string.IsNullOrWhiteSpace(e.SearchKeyword))
                .GroupBy(e => e.SearchKeyword!)
                .Select(g => new { Keyword = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GoogleAnalyticsHealth()
        {
            var status = await _googleAnalyticsService.CheckConnectionAsync();
            return Json(new
            {
                isEnabled = status.IsEnabled,
                isConfigured = status.IsConfigured,
                isConnected = status.IsConnected,
                level = status.Level,
                title = status.Title,
                message = status.Message,
                measurementId = status.MeasurementId,
                propertyId = status.PropertyId,
                credentialsFileExists = status.CredentialsFileExists,
                technicalDetail = status.TechnicalDetail,
                checkedAt = status.CheckedAt.ToString("dd/MM/yyyy HH:mm:ss")
            });
        }

        private static decimal Rate(int value, int total)
        {
            if (total <= 0) return 0m;
            return Math.Round((decimal)value / total * 100m, 2);
        }
    }
}
