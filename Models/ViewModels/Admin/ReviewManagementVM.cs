using System;
using System.Collections.Generic;

namespace TMDT_LT.Models.ViewModels
{
    // CỘT BÊN TRÁI: Thống kê tổng quan đánh giá theo từng sản phẩm để gom nhóm
    public class ReviewGroupVM
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductImage { get; set; }

        // Số lượng đánh giá chưa đọc để làm hiển thị Badge số thông báo đỏ
        public int UnreadCount { get; set; }

        // Tổng số đánh giá của riêng sản phẩm này (Fix lỗi CS0117)
        public int TotalCount { get; set; }

        // Mốc thời gian tương tác cuối cùng để đẩy sản phẩm lên top 1 danh sách
        public DateTime LatestReviewDate { get; set; }
    }

    // CỘT BÊN PHẢI: Chi tiết từng dòng đánh giá của sản phẩm đang được chọn
    public class ProductReviewDetailVM
    {
        public int ReviewId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        public int? OrderId { get; set; } // Mã đơn hàng xác thực để phục vụ đối soát
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
        public bool IsHidden { get; set; }
        public string? AdminReply { get; set; }
    }
}
