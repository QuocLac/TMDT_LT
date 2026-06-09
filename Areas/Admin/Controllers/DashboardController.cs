using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        public DashboardController(ApplicationDbContext context) => _context = context;

        public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate)
        {
            // Thiết lập khoảng ngày mặc định nếu Admin chưa chọn bộ lọc (Mặc định là 30 ngày qua)
            if (!fromDate.HasValue) fromDate = DateTime.Today.AddDays(-30);
            if (!toDate.HasValue) toDate = DateTime.Today.AddDays(1).AddSeconds(-1); // Cuối ngày hôm nay

            ViewBag.FromDate = fromDate.Value.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate.Value.ToString("yyyy-MM-dd");

            // 1. Kéo toàn bộ đơn hàng trong khoảng thời gian lọc về bộ nhớ để phân tích đa chiều
            var orders = await _context.Orders
                .Include(o => o.OrderDetails)
                .Where(o => o.OrderDate >= fromDate && o.OrderDate <= toDate)
                .ToListAsync();

            int totalOrders = orders.Count;
            var completedOrders = orders.Where(o => o.Status == "Hoàn thành").ToList();
            var canceledOrders = orders.Where(o => o.Status == "Đã hủy").ToList();

            // Tính các chỉ số Core Metrics
            decimal totalRevenue = completedOrders.Sum(o => o.TotalAmount ?? 0);
            decimal totalLosses = canceledOrders.Sum(o => o.TotalAmount ?? 0); // Tổn thất do hủy đơn (Doanh thu hụt)
            double boomRate = totalOrders > 0 ? Math.Round((double)canceledOrders.Count / totalOrders * 100, 1) : 0;

            // 2. THUẬT TOÁN TÍNH GIÁ VỐN HÀNG BÁN (COGS) & LỢI NHUẬN THUẦN
            decimal totalCostOfGoodsSold = 0;
            var completedOrderIds = completedOrders.Select(o => o.OrderId).ToList();

            var orderDetails = await _context.OrderDetails
                .Where(od => completedOrderIds.Contains((int)od.OrderId))
                .ToListAsync();

            // Lấy toàn bộ lịch sử giá nhập để tính toán tối ưu trên RAM
            var purchaseDetails = await _context.PurchaseOrderDetails.ToListAsync();

            foreach (var detail in orderDetails)
            {
                // Lấy giá nhập trung bình lịch sử của biến thể sản phẩm này
                var matchPrices = purchaseDetails.Where(p => p.VariantId == detail.VariantId).Select(p => p.ImportPrice).ToList();
                decimal avgImportPrice = matchPrices.Any() ? matchPrices.Average() : (detail.UnitPrice ?? 0) * 0.75m; // Nếu chưa nhập kho bao giờ, giả định giá vốn bằng 75% giá bán

                totalCostOfGoodsSold += (avgImportPrice * (detail.Quantity ?? 0));
            }

            decimal netProfit = totalRevenue - totalCostOfGoodsSold;

            // Đổ số liệu ra màn hình Dashboard Cards
            ViewBag.TotalOrders = totalOrders;
            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.NetProfit = netProfit;
            ViewBag.TotalLosses = totalLosses;
            ViewBag.BoomRate = boomRate;

            // 3. ĐỒNG BỘ DỮ LIỆU SANG BIỂU ĐỒ (CHART DATA SERIALIZATION)
            // Biểu đồ tròn: Tỷ lệ trạng thái đơn hàng
            ViewBag.StatusLabels = new[] { "Hoàn thành", "Chờ xác nhận", "Đang giao hàng", "Đã hủy" };
            ViewBag.StatusData = new[] {
                orders.Count(o => o.Status == "Hoàn thành"),
                orders.Count(o => o.Status == "Chờ xác nhận"),
                orders.Count(o => o.Status == "Đang giao hàng"),
                orders.Count(o => o.Status == "Đã hủy")
            };

            // Biểu đồ cột & đường: Tiến trình tăng trưởng doanh thu theo ngày
            var timelineData = completedOrders
                .GroupBy(o => o.OrderDate.Value.Date)
                .OrderBy(g => g.Key)
                .Select(g => new {
                    Date = g.Key.ToString("dd/MM"),
                    Revenue = g.Sum(o => o.TotalAmount ?? 0)
                }).ToList();

            ViewBag.TimelineLabels = timelineData.Select(t => t.Date).ToArray();
            ViewBag.TimelineRevenue = timelineData.Select(t => t.Revenue).ToArray();

            return View();
        }
    }
}