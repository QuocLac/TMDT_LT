using System;

namespace TMDT_LT.Models
{
    public class UserBehaviorLog
    {
        public int Id { get; set; }

        // ID của khách hàng (null nếu khách chưa đăng nhập / ẩn danh)
        public int? CustomerId { get; set; }

        // Sản phẩm đang tương tác
        public int ProductId { get; set; }

        // Sản phẩm đích (nếu click từ vùng UpSellProducts)
        public int? TargetProductId { get; set; }

        // Thời gian xem sản phẩm (tính bằng giây)
        public int ViewDuration { get; set; }

        // Từ khóa tìm kiếm dẫn dắt user đến sản phẩm này
        public string? SearchKeyword { get; set; }

        // Loại hành vi: "ViewDuration" hoặc "ProductClick"
        public string ActionType { get; set; } = "ViewDuration";

        // Mốc thời gian hệ thống ghi nhận
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}