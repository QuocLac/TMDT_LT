using System;
using System.Collections.Generic;

namespace TMDT_LT.Models.ViewModels
{
    public class DashboardVM
    {
        public string FilterStartDate { get; set; } = string.Empty;
        public string FilterEndDate { get; set; } = string.Empty;

        // Financial KPIs
        public decimal GrossRevenue { get; set; }
        public decimal NetRevenue { get; set; }
        public decimal TotalCost { get; set; }
        public decimal TotalNetProfit { get; set; }
        public double GrossMarginRate { get; set; }
        public decimal AverageOrderValue { get; set; }
        public decimal RevenueThisMonth { get; set; }
        public decimal RevenueLastMonth { get; set; }
        public double RevenueMoMGrowth { get; set; }

        // Order KPIs
        public int TotalOrdersCount { get; set; }
        public int CompletedOrdersCount { get; set; }
        public int PaidOrdersCount { get; set; }
        public int CancelledOrdersCount { get; set; }
        public int ReturnedOrdersCount { get; set; }
        public decimal RefundRiskAmount { get; set; }
        public double CancelRate { get; set; }
        public double ReturnRate { get; set; }

        // Customer / catalog KPIs
        public int TotalCustomersCount { get; set; }
        public int NewCustomersCount { get; set; }
        public int ActiveCustomersCount { get; set; }
        public int ChurnedCustomersCount { get; set; }
        public double ChurnRate { get; set; }
        public int TotalProductsCount { get; set; }
        public int LowStockVariantCount { get; set; }
        public int InventoryShrinkage { get; set; }
        public int TotalQuantitySold { get; set; }
        public double AverageDeliveryHours { get; set; }

        // Behavior bridge KPIs from internal analytics
        public int FunnelViews { get; set; }
        public int ProductViews { get; set; }
        public int FunnelCarts { get; set; }
        public int FunnelCheckouts { get; set; }
        public int FunnelOrders { get; set; }
        public double ConversionRate { get; set; }
        public double CartToOrderRate { get; set; }

        // Charts / tables
        public List<DailyFinancialVM> FinancialChartData { get; set; } = new List<DailyFinancialVM>();
        public List<DailyTrafficVM> TrafficChartData { get; set; } = new List<DailyTrafficVM>();
        public List<ProductSalesStatsVM> ProductSalesStats { get; set; } = new List<ProductSalesStatsVM>();
        public List<PaymentMethodStatsVM> PaymentMethodStats { get; set; } = new List<PaymentMethodStatsVM>();
        public List<OrderStatusStatsVM> OrderStatusStats { get; set; } = new List<OrderStatusStatsVM>();
    }

    public class DailyFinancialVM
    {
        public string DateLabel { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal PurchaseCost { get; set; }
        public decimal NetProfit { get; set; }
        public int OrderCount { get; set; }
    }

    public class DailyTrafficVM
    {
        public string DateLabel { get; set; } = string.Empty;
        public int PageViews { get; set; }
        public int ProductViews { get; set; }
        public int AddToCarts { get; set; }
        public int Checkouts { get; set; }
        public int Purchases { get; set; }
    }

    public class ProductSalesStatsVM
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string BrandName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int QuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalCost { get; set; }
        public decimal NetProfit { get; set; }
        public decimal AverageSellingPrice { get; set; }
        public double MarginRate { get; set; }
    }

    public class PaymentMethodStatsVM
    {
        public string PaymentMethod { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal Revenue { get; set; }
        public double Rate { get; set; }
    }

    public class OrderStatusStatsVM
    {
        public string Status { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public double Rate { get; set; }
    }
}
