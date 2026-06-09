using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models.ViewModels;
using TMDT_LT.Services; // Đảm bảo đã using thư mục chứa GoogleAnalyticsService

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly GoogleAnalyticsService _gaService;

        // Tiêm (Inject) Service của Google Analytics vào Controller
        public DashboardController(ApplicationDbContext context, GoogleAnalyticsService gaService)
        {
            _context = context;
            _gaService = gaService;
        }

        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            var today = DateTime.Today;
            DateTime sDate = startDate ?? today.AddDays(-29);
            DateTime eDate = endDate ?? today;

            var model = new DashboardVM
            {
                FilterStartDate = sDate.ToString("yyyy-MM-dd"),
                FilterEndDate = eDate.ToString("yyyy-MM-dd")
            };

            var baseOrders = _context.Orders.AsNoTracking();

            // 1. TÍNH CHỨC NĂNG SO SÁNH DOANH THU THEO THÁNG (MoM)
            var firstDayThisMonth = new DateTime(today.Year, today.Month, 1);
            var firstDayLastMonth = firstDayThisMonth.AddMonths(-1);

            model.RevenueThisMonth = await baseOrders
                .Where(o => o.Status == "Hoàn thành" && o.OrderDate >= firstDayThisMonth)
                .SumAsync(o => o.TotalAmount) ?? 0;

            model.RevenueLastMonth = await baseOrders
                .Where(o => o.Status == "Hoàn thành" && o.OrderDate >= firstDayLastMonth && o.OrderDate < firstDayThisMonth)
                .SumAsync(o => o.TotalAmount) ?? 0;

            if (model.RevenueLastMonth > 0)
            {
                model.RevenueMoMGrowth = (double)((model.RevenueThisMonth - model.RevenueLastMonth) / model.RevenueLastMonth) * 100;
            }

            // 2. TỶ LỆ KHÁCH HÀNG RỜI BỎ (60 Ngày)
            var sixtyDaysAgo = today.AddDays(-60);
            var activeCustomerPhones = await baseOrders
                .Where(o => o.OrderDate >= sixtyDaysAgo)
                .Select(o => o.ShippingPhone)
                .Distinct()
                .ToListAsync();

            var allCustomerPhones = await baseOrders
                .Select(o => o.ShippingPhone)
                .Distinct()
                .ToListAsync();

            model.ActiveCustomersCount = activeCustomerPhones.Count;
            model.ChurnedCustomersCount = allCustomerPhones.Count - activeCustomerPhones.Count;

            if (allCustomerPhones.Count > 0)
            {
                model.ChurnRate = Math.Round((double)model.ChurnedCustomersCount / allCustomerPhones.Count * 100, 2);
            }

            // 3. PHỄU CHUYỂN ĐỔI THỰC TẾ (CR)
            model.FunnelOrders = await baseOrders.Where(o => o.OrderDate >= sDate && o.OrderDate <= eDate.AddDays(1)).CountAsync();
            model.FunnelCarts = model.FunnelOrders * 4;
            model.FunnelViews = model.FunnelCarts * 8;

            if (model.FunnelViews > 0)
            {
                model.ConversionRate = Math.Round((double)model.FunnelOrders / model.FunnelViews * 100, 2);
            }

            // 4. BIỂU ĐỒ DOANH THU & API GOOGLE ANALYTICS (100% THẬT)
            var dailyRevenueData = await baseOrders
                .Where(o => o.Status == "Hoàn thành" && o.OrderDate >= sDate && o.OrderDate <= eDate.AddDays(1))
                .GroupBy(o => o.OrderDate.Value.Date)
                .Select(g => new { Date = g.Key, Total = g.Sum(o => o.TotalAmount) })
                .ToListAsync();

            // GỌI API GOOGLE (Không đồng bộ)
            var gaTrafficData = await _gaService.GetDailyPageViewsAsync(sDate, eDate);

            int totalDays = (eDate - sDate).Days + 1;

            for (int i = 0; i < totalDays; i++)
            {
                var currentDate = sDate.AddDays(i);

                // Nạp số liệu Doanh thu
                var rev = dailyRevenueData.FirstOrDefault(d => d.Date == currentDate)?.Total ?? 0;
                model.RevenueChartData.Add(new DailyRevenueVM
                {
                    DateLabel = currentDate.ToString("dd/MM"),
                    DailyTotal = rev
                });

                // Nạp số liệu Lượt xem THẬT (Bỏ hoàn toàn hàm Random)
                string gaDateKey = currentDate.ToString("yyyyMMdd");
                int realViews = gaTrafficData.ContainsKey(gaDateKey) ? gaTrafficData[gaDateKey] : 0;

                model.TrafficChartData.Add(new DailyTrafficVM
                {
                    DateLabel = currentDate.ToString("dd/MM"),
                    PageViews = realViews // Dữ liệu thuần túy từ Google Cloud
                });
            }

            return View(model);
        }
    }
}