using System;
using System.Collections.Generic;

namespace TMDT_LT.Models.ViewModels
{
    // Cột bên trái: Danh sách các Sản phẩm đang chứa đánh giá
    public class ProductReviewGroupVM
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductImage { get; set; }

        // Thuật toán: Số lượng huy hiệu đỏ (Bình luận mới)
        public int UnreadCount { get; set; }

        // Thuật toán: Lấy thời gian bình luận gần nhất để Bump (đẩy) sản phẩm lên đầu
        public DateTime LatestReviewDate { get; set; }

        public int TotalReviews { get; set; }
    }

    // Cột bên phải: Gói dữ liệu chứa chi tiết danh sách đánh giá của 1 sản phẩm
    public class ReviewDetailVM
    {
        public int ReviewId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
        public bool IsHidden { get; set; }
        public string? AdminReply { get; set; }
    }
}