using System;
using System.Collections.Generic;

namespace TMDT_LT.Models.ViewModels
{
    // LỚP TỔNG HỢP DỮ LIỆU DASHBOARD CHÍNH
    public class DashboardVM
    {
        // Bộ lọc thời gian phản hồi
        public string FilterStartDate { get; set; } = string.Empty;
        public string FilterEndDate { get; set; } = string.Empty;

        // 1. Chỉ số Tài chính & So sánh tháng trước (MoM)
        public decimal RevenueThisMonth { get; set; }
        public decimal RevenueLastMonth { get; set; }
        public double RevenueMoMGrowth { get; set; } // % tăng trưởng doanh thu

        // 2. Tỷ lệ khách hàng rời bỏ (Churn Rate - 2 tháng không mua sắm)
        public double ChurnRate { get; set; }
        public int ActiveCustomersCount { get; set; }
        public int ChurnedCustomersCount { get; set; }

        // 3. Tỷ lệ chuyển đổi (Conversion Funnel)
        public int FunnelViews { get; set; }       // Lượt xem sản phẩm
        public int FunnelCarts { get; set; }       // Lượt thêm giỏ hàng
        public int FunnelOrders { get; set; }      // Lượt đặt hàng thành công
        public double ConversionRate { get; set; } // Tỷ lệ chuyển đổi cuối cùng (%)

        // 4. Mảng dữ liệu biểu đồ
        public List<DailyRevenueVM> RevenueChartData { get; set; } = new List<DailyRevenueVM>();
        public List<DailyTrafficVM> TrafficChartData { get; set; } = new List<DailyTrafficVM>();
    }

    // --- CÁC LỚP PHỤ TRỢ (SUB-MODELS) ĐỂ ĐỔ DỮ LIỆU VÀO BIỂU ĐỒ ---

    // Lớp dữ liệu biểu đồ doanh thu
    public class DailyRevenueVM
    {
        public string DateLabel { get; set; } = string.Empty;
        public decimal DailyTotal { get; set; }
    }

    // Lớp dữ liệu biểu đồ truy cập
    public class DailyTrafficVM
    {
        public string DateLabel { get; set; } = string.Empty;
        public int PageViews { get; set; }
    }
}