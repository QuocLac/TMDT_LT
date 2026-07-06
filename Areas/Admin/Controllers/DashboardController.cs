using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models.ViewModels;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            var today = DateTime.Today;
            DateTime sDate = (startDate ?? today.AddDays(-29)).Date;
            DateTime eDate = (endDate ?? today).Date;
            DateTime endExclusive = eDate.AddDays(1);

            var revenueStatuses = new[] { "Hoàn thành", "Đã giao", "Đã nhận hàng" };
            var paidStatuses = new[] { "Đã thanh toán", "Hoàn thành", "Paid", "Success", "Thành công" };
            var returnDoneStatuses = new[] { "Đã chấp nhận", "Hoàn tiền thành công", "Hoàn tất", "Đã hoàn tiền" };

            var model = new DashboardVM
            {
                FilterStartDate = sDate.ToString("yyyy-MM-dd"),
                FilterEndDate = eDate.ToString("yyyy-MM-dd")
            };

            var orderBase = _context.Orders.AsNoTracking();
            var ordersInPeriod = orderBase.Where(o => o.OrderDate >= sDate && o.OrderDate < endExclusive);
            var revenueOrdersQuery = ordersInPeriod.Where(o => o.Status != null && revenueStatuses.Contains(o.Status));

            model.TotalCustomersCount = await _context.Customer.CountAsync();
            model.TotalProductsCount = await _context.Products.CountAsync(p => p.IsActive == true);
            model.LowStockVariantCount = await _context.ProductVariants.CountAsync(v => v.IsActive == true && (v.Stock ?? 0) > 0 && (v.Stock ?? 0) <= 5);
            model.TotalOrdersCount = await ordersInPeriod.CountAsync();
            model.CompletedOrdersCount = await revenueOrdersQuery.CountAsync();
            model.CancelledOrdersCount = await ordersInPeriod.CountAsync(o => o.Status == "Đã hủy");
            model.CancelRate = model.TotalOrdersCount > 0 ? model.CancelledOrdersCount * 100.0 / model.TotalOrdersCount : 0;

            model.NewCustomersCount = await _context.Customer.CountAsync(c => c.Account != null && c.Account.CreatedAt >= sDate && c.Account.CreatedAt < endExclusive);

            var twoMonthsAgo = today.AddMonths(-2);
            model.ActiveCustomersCount = await _context.Customer
                .CountAsync(c => _context.Orders.Any(o => o.CustomerId == c.CustomerId && o.OrderDate >= twoMonthsAgo));
            model.ChurnedCustomersCount = await _context.Customer
                .CountAsync(c => !_context.Orders.Any(o => o.CustomerId == c.CustomerId && o.OrderDate >= twoMonthsAgo));
            var totalCustomerBase = model.ActiveCustomersCount + model.ChurnedCustomersCount;
            model.ChurnRate = totalCustomerBase > 0 ? model.ChurnedCustomersCount * 100.0 / totalCustomerBase : 0;

            var thisMonthStart = new DateTime(today.Year, today.Month, 1);
            var lastMonthStart = thisMonthStart.AddMonths(-1);
            model.RevenueThisMonth = await orderBase
                .Where(o => o.Status != null && revenueStatuses.Contains(o.Status) && o.OrderDate >= thisMonthStart)
                .SumAsync(o => o.TotalAmount ?? 0m);
            model.RevenueLastMonth = await orderBase
                .Where(o => o.Status != null && revenueStatuses.Contains(o.Status) && o.OrderDate >= lastMonthStart && o.OrderDate < thisMonthStart)
                .SumAsync(o => o.TotalAmount ?? 0m);
            model.RevenueMoMGrowth = model.RevenueLastMonth > 0
                ? (double)((model.RevenueThisMonth - model.RevenueLastMonth) / model.RevenueLastMonth) * 100
                : model.RevenueThisMonth > 0 ? 100 : 0;

            var completedOrders = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Payments)
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Variant)
                    .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Brand)
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Variant)
                    .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.Category)
                .Where(o => o.Status != null && revenueStatuses.Contains(o.Status) && o.OrderDate >= sDate && o.OrderDate < endExclusive)
                .ToListAsync();

            model.GrossRevenue = completedOrders.Sum(o => o.TotalAmount ?? 0m);
            model.TotalQuantitySold = completedOrders.SelectMany(o => o.OrderDetails).Sum(d => d.Quantity ?? 0);
            model.AverageOrderValue = model.CompletedOrdersCount > 0 ? model.GrossRevenue / model.CompletedOrdersCount : 0m;

            var paidOrderIds = await _context.Payments
                .AsNoTracking()
                .Where(p => p.PaymentStatus != null && paidStatuses.Contains(p.PaymentStatus) && p.Order != null && p.Order.OrderDate >= sDate && p.Order.OrderDate < endExclusive)
                .Select(p => p.OrderId)
                .Distinct()
                .CountAsync();
            model.PaidOrdersCount = paidOrderIds;

            var returnOrders = await _context.OrderReturns
                .AsNoTracking()
                .Include(r => r.Order)
                .Where(r => returnDoneStatuses.Contains(r.Status) && r.CreatedAt >= sDate && r.CreatedAt < endExclusive)
                .ToListAsync();
            model.ReturnedOrdersCount = returnOrders.Count;
            model.RefundRiskAmount = returnOrders.Sum(r => r.Order?.TotalAmount ?? 0m);
            model.ReturnRate = model.CompletedOrdersCount > 0 ? model.ReturnedOrdersCount * 100.0 / model.CompletedOrdersCount : 0;
            model.NetRevenue = model.GrossRevenue - model.RefundRiskAmount;

            var importPrices = await _context.PurchaseOrderDetails
                .AsNoTracking()
                .GroupBy(pod => pod.VariantId)
                .Select(g => new
                {
                    VariantId = g.Key,
                    LatestPrice = g.OrderByDescending(x => x.PodetailId).Select(x => x.ImportPrice).FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.VariantId, x => x.LatestPrice);

            var productStats = new Dictionary<int, ProductSalesStatsVM>();
            var dailyFinancial = new Dictionary<DateTime, DailyFinancialVM>();

            foreach (var order in completedOrders)
            {
                var orderDate = (order.OrderDate ?? sDate).Date;
                if (!dailyFinancial.ContainsKey(orderDate))
                {
                    dailyFinancial[orderDate] = new DailyFinancialVM
                    {
                        DateLabel = orderDate.ToString("dd/MM"),
                        Revenue = 0m,
                        PurchaseCost = 0m,
                        NetProfit = 0m,
                        OrderCount = 0
                    };
                }

                dailyFinancial[orderDate].Revenue += order.TotalAmount ?? 0m;
                dailyFinancial[orderDate].OrderCount += 1;

                foreach (var detail in order.OrderDetails)
                {
                    if (detail.Variant?.Product == null) continue;

                    int quantity = detail.Quantity ?? 0;
                    decimal unitPrice = detail.UnitPrice ?? 0m;
                    int variantId = detail.VariantId ?? 0;
                    int productId = detail.Variant.ProductId;
                    decimal costPrice = importPrices.TryGetValue(variantId, out var latestPrice) && latestPrice > 0m
                        ? latestPrice
                        : unitPrice * 0.7m;

                    decimal lineRevenue = unitPrice * quantity;
                    decimal lineCost = costPrice * quantity;
                    decimal lineProfit = lineRevenue - lineCost;

                    if (!productStats.ContainsKey(productId))
                    {
                        productStats[productId] = new ProductSalesStatsVM
                        {
                            ProductId = productId,
                            ProductName = detail.Variant.Product.Name,
                            BrandName = detail.Variant.Product.Brand?.BrandName ?? "Chưa rõ",
                            CategoryName = detail.Variant.Product.Category?.CategoryName ?? "Chưa rõ"
                        };
                    }

                    productStats[productId].QuantitySold += quantity;
                    productStats[productId].TotalRevenue += lineRevenue;
                    productStats[productId].TotalCost += lineCost;
                    productStats[productId].NetProfit += lineProfit;

                    dailyFinancial[orderDate].PurchaseCost += lineCost;
                    dailyFinancial[orderDate].NetProfit += lineProfit;
                }
            }

            foreach (var item in productStats.Values)
            {
                item.AverageSellingPrice = item.QuantitySold > 0 ? item.TotalRevenue / item.QuantitySold : 0m;
                item.MarginRate = item.TotalRevenue > 0 ? (double)(item.NetProfit / item.TotalRevenue) * 100 : 0;
            }

            model.ProductSalesStats = productStats.Values
                .OrderByDescending(x => x.TotalRevenue)
                .ThenByDescending(x => x.QuantitySold)
                .Take(15)
                .ToList();

            model.TotalCost = model.ProductSalesStats.Sum(x => x.TotalCost);
            model.TotalNetProfit = model.ProductSalesStats.Sum(x => x.NetProfit) - model.RefundRiskAmount;
            model.GrossMarginRate = model.GrossRevenue > 0 ? (double)(model.TotalNetProfit / model.GrossRevenue) * 100 : 0;
            model.InventoryShrinkage = (int)(await _context.ProductVariants.SumAsync(v => v.Stock ?? 0) * 0.005);

            var deliveredShipments = await _context.Shipping
                .AsNoTracking()
                .Where(s => s.ShippedDate != null && s.DeliveredDate != null && s.DeliveredDate >= sDate && s.DeliveredDate < endExclusive)
                .Select(s => new { s.ShippedDate, s.DeliveredDate })
                .ToListAsync();
            model.AverageDeliveryHours = deliveredShipments.Any()
                ? deliveredShipments.Average(s => (s.DeliveredDate!.Value - s.ShippedDate!.Value).TotalHours)
                : 0;

            var paymentRows = await _context.Payments
                .AsNoTracking()
                .Include(p => p.Order)
                .Where(p => p.Order != null && p.Order.Status != null && revenueStatuses.Contains(p.Order.Status) && p.Order.OrderDate >= sDate && p.Order.OrderDate < endExclusive)
                .ToListAsync();

            model.PaymentMethodStats = paymentRows
                .GroupBy(p => p.PaymentMethod ?? "Chưa xác định")
                .Select(g =>
                {
                    decimal revenue = g.Sum(x => x.Order?.TotalAmount ?? 0m);
                    return new PaymentMethodStatsVM
                    {
                        PaymentMethod = g.Key,
                        OrderCount = g.Select(x => x.OrderId).Distinct().Count(),
                        Revenue = revenue,
                        Rate = model.GrossRevenue > 0 ? (double)(revenue / model.GrossRevenue) * 100 : 0
                    };
                })
                .OrderByDescending(x => x.Revenue)
                .ToList();

            var statusStatsRaw = await ordersInPeriod
                .GroupBy(o => o.Status ?? "Chưa xác định")
                .Select(g => new { Status = g.Key, OrderCount = g.Count() })
                .OrderByDescending(x => x.OrderCount)
                .ToListAsync();
            model.OrderStatusStats = statusStatsRaw.Select(x => new OrderStatusStatsVM
            {
                Status = x.Status,
                OrderCount = x.OrderCount,
                Rate = model.TotalOrdersCount > 0 ? x.OrderCount * 100.0 / model.TotalOrdersCount : 0
            }).ToList();

            var eventsInPeriod = _context.AnalyticsEvents.AsNoTracking()
                .Where(e => e.CreatedAt >= sDate && e.CreatedAt < endExclusive);

            model.FunnelViews = await eventsInPeriod.CountAsync(e => e.EventName == "page_view_internal" || e.EventName == "page_view");
            model.ProductViews = await eventsInPeriod.CountAsync(e => e.EventName == "view_item");
            model.FunnelCarts = await eventsInPeriod.CountAsync(e => e.EventName == "add_to_cart");
            model.FunnelCheckouts = await eventsInPeriod.CountAsync(e => e.EventName == "begin_checkout");
            var internalPurchases = await eventsInPeriod.CountAsync(e => e.EventName == "purchase");
            model.FunnelOrders = internalPurchases > 0 ? internalPurchases : model.CompletedOrdersCount;
            model.ConversionRate = model.FunnelViews > 0 ? model.FunnelOrders * 100.0 / model.FunnelViews : 0;
            model.CartToOrderRate = model.FunnelCarts > 0 ? model.FunnelOrders * 100.0 / model.FunnelCarts : 0;

            var eventList = await eventsInPeriod
                .Select(e => new { e.CreatedAt, e.EventName })
                .ToListAsync();
            var eventDaily = eventList
                .GroupBy(e => e.CreatedAt.Date)
                .ToDictionary(g => g.Key, g => new
                {
                    PageViews = g.Count(x => x.EventName == "page_view_internal" || x.EventName == "page_view"),
                    ProductViews = g.Count(x => x.EventName == "view_item"),
                    AddToCarts = g.Count(x => x.EventName == "add_to_cart"),
                    Checkouts = g.Count(x => x.EventName == "begin_checkout"),
                    Purchases = g.Count(x => x.EventName == "purchase")
                });

            var totalDays = (eDate - sDate).Days + 1;
            for (int i = 0; i < totalDays; i++)
            {
                var day = sDate.AddDays(i);
                if (!dailyFinancial.TryGetValue(day, out var daily))
                {
                    daily = new DailyFinancialVM { DateLabel = day.ToString("dd/MM") };
                }

                model.FinancialChartData.Add(daily);

                if (eventDaily.TryGetValue(day, out var ev))
                {
                    model.TrafficChartData.Add(new DailyTrafficVM
                    {
                        DateLabel = day.ToString("dd/MM"),
                        PageViews = ev.PageViews,
                        ProductViews = ev.ProductViews,
                        AddToCarts = ev.AddToCarts,
                        Checkouts = ev.Checkouts,
                        Purchases = ev.Purchases
                    });
                }
                else
                {
                    model.TrafficChartData.Add(new DailyTrafficVM { DateLabel = day.ToString("dd/MM") });
                }
            }

            return View(model);
        }
    }
}
