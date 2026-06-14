using System;
using System.Collections.Generic;

namespace TMDT_LT.Models.ViewModels
{
    public class DashboardVM
    {
        public string FilterStartDate { get; set; } = string.Empty;
        public string FilterEndDate { get; set; } = string.Empty;

        // --- HỆ THỐNG CHỈ SỐ THẺ CARD (KPI CARDS) ---
        public int TotalCustomersCount { get; set; }     // Tổng số lượng khách hàng đăng ký
        public int TotalProductsCount { get; set; }      // Tổng sản phẩm đăng bán (Active)
        public double ChurnRate { get; set; }            // Tỷ lệ rời bỏ (Tỷ lệ CRU đối nghịch)
        public int ActiveCustomersCount { get; set; }    // Khách hàng hoạt động
        public int ChurnedCustomersCount { get; set; }   // Khách hàng ngưng tương tác
        public decimal RevenueThisMonth { get; set; }    // Doanh thu tháng này
        public decimal RevenueLastMonth { get; set; }    // Doanh thu tháng trước
        public double RevenueMoMGrowth { get; set; }     // Tỷ lệ phần trăm so sánh doanh thu MoM
        public int CancelledOrdersCount { get; set; }    // Số lượng đơn hàng bị hủy
        public int ReturnedOrdersCount { get; set; }     // Số lượng đơn hàng hoàn trả
        public decimal TotalNetProfit { get; set; }      // Tổng Lợi nhuận ròng
        public int TotalQuantitySold { get; set; }       // Tổng số lượng máy đã bán
        public int InventoryShrinkage { get; set; }      // Hao hụt kho

        // --- HỆ THỐNG DỮ LIỆU PHỄU CHUYỂN ĐỔI & TRAFFIC ---
        public int FunnelViews { get; set; }
        public int FunnelCarts { get; set; }
        public int FunnelOrders { get; set; }
        public double ConversionRate { get; set; }

        // --- MẢNG ĐỒNG BỘ CÁC BIỂU ĐỒ XU HƯỚNG ---
        public List<DailyFinancialVM> FinancialChartData { get; set; } = new List<DailyFinancialVM>();
        public List<DailyTrafficVM> TrafficChartData { get; set; } = new List<DailyTrafficVM>();

        // --- DANH SÁCH BẢNG SỐ LIỆU SẢN PHẨM CỤ THỂ ---
        public List<ProductSalesStatsVM> ProductSalesStats { get; set; } = new List<ProductSalesStatsVM>();
    }

    public class DailyFinancialVM
    {
        public string DateLabel { get; set; } = string.Empty;
        public decimal Revenue { get; set; }      // Tổng doanh thu hằng ngày
        public decimal PurchaseCost { get; set; } // Tổng tiền nhập kho hằng ngày
        public decimal NetProfit { get; set; }    // Lợi nhuận hằng ngày (Doanh thu - Nhập kho)
    }

    public class DailyTrafficVM
    {
        public string DateLabel { get; set; } = string.Empty;
        public int PageViews { get; set; }
    }

    public class ProductSalesStatsVM
    {
        public string ProductName { get; set; } = string.Empty;
        public int QuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalCost { get; set; }
        public decimal NetProfit { get; set; }
    }
}