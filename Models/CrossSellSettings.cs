using System;
using System.ComponentModel.DataAnnotations;

namespace TMDT_LT.Models
{
    public class CrossSellSettings
    {
        public int CrossSellSettingsId { get; set; }

        [Display(Name = "Bật gợi ý mua kèm")]
        public bool IsEnabled { get; set; } = true;

        [Range(1, 100000)]
        [Display(Name = "Số đơn mua kèm tối thiểu")]
        public int MinSupportCount { get; set; } = 2;

        [Range(0, 1)]
        [Display(Name = "Tỷ lệ xuất hiện tối thiểu")]
        public decimal MinSupportPercent { get; set; } = 0m;

        [Range(0, 1)]
        [Display(Name = "Tỷ lệ chọn mua kèm tối thiểu")]
        public decimal MinConfidence { get; set; } = 0.2m;

        [Range(0, 100)]
        [Display(Name = "Độ liên quan tối thiểu")]
        public decimal MinLift { get; set; } = 1m;

        [Range(2, 4)]
        [Display(Name = "Số sản phẩm tối đa trong một nhóm phân tích")]
        public int MaxItemsetSize { get; set; } = 3;

        [Range(1, 24)]
        [Display(Name = "Số gợi ý tối đa mỗi sản phẩm")]
        public int MaxRecommendationsPerProduct { get; set; } = 6;

        [Range(0, 3650)]
        [Display(Name = "Khoảng thời gian lấy dữ liệu")]
        public int AnalysisWindowDays { get; set; } = 365;

        [Display(Name = "Chỉ dùng đơn đã hoàn tất")]
        public bool OnlyCompletedOrders { get; set; } = true;

        [StringLength(500)]
        [Display(Name = "Trạng thái đơn được tính")]
        public string AllowedOrderStatuses { get; set; } = "Đã hoàn thành,Hoàn thành,Đã giao,Completed";

        [Display(Name = "Ẩn sản phẩm hết hàng")]
        public bool ExcludeOutOfStock { get; set; } = true;

        [Display(Name = "Dùng sản phẩm cùng danh mục khi chưa đủ dữ liệu")]
        public bool AllowFallbackWhenNoRule { get; set; } = false;

        [Display(Name = "Bật ưu đãi cho sản phẩm mua kèm")]
        public bool IsBundleDiscountEnabled { get; set; } = false;

        // 0: Giảm theo %, 1: Giảm số tiền cố định
        [Range(0, 1)]
        [Display(Name = "Kiểu giảm giá mua kèm")]
        public int BundleDiscountType { get; set; } = 0;

        [Range(0, 100000000)]
        [Display(Name = "Mức giảm cho sản phẩm mua kèm")]
        public decimal BundleDiscountValue { get; set; } = 0m;

        [StringLength(120)]
        [Display(Name = "Nhãn ưu đãi mua kèm")]
        public string BundleDiscountLabel { get; set; } = "Ưu đãi mua kèm";

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public int? UpdatedByAccountId { get; set; }
    }
}
