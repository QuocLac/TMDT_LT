using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ReviewController : Controller
    {
        private readonly ApplicationDbContext _context;
        public ReviewController(ApplicationDbContext context) => _context = context;

        // 1. Màn hình chính: Tải danh sách sản phẩm gom nhóm (Cột Trái)
        public async Task<IActionResult> Index()
        {
            var productGroups = await _context.Reviews
                .Where(r => r.ProductId != null)
                .GroupBy(r => r.ProductId)
                .Select(g => new ReviewGroupVM
                {
                    ProductId = g.Key!.Value,
                    ProductName = g.First().Product != null ? g.First().Product!.Name : "Sản phẩm ẩn",
                    UnreadCount = g.Count(r => !r.IsRead),
                    TotalCount = g.Count(),
                    LatestReviewDate = g.Max(r => r.CreatedAt) ?? DateTime.Now
                })
                .OrderByDescending(g => g.LatestReviewDate)
                .ToListAsync();

            return View(productGroups);
        }

        // 2. API AJAX: Lấy danh sách đánh giá chi tiết theo sản phẩm kèm bộ lọc thích ứng
        [HttpGet]
        public async Task<IActionResult> GetReviewsByProduct(int productId, int? star, string? status)
        {
            var query = _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.Order)
                .Include(r => r.ReviewDetails).ThenInclude(rd => rd.Variant) // Nối bảng lấy Multimedia và Biến thể phần cứng
                .Where(r => r.ProductId == productId);

            // Bộ lọc 1: Lọc theo số sao (1-5 sao)
            if (star.HasValue && star.Value >= 1 && star.Value <= 5)
            {
                query = query.Where(r => r.Rating == star.Value);
            }

            // Bộ lọc 2: Lọc theo trạng thái kiểm duyệt Ẩn/Hiện
            if (!string.IsNullOrEmpty(status))
            {
                if (status == "hidden") query = query.Where(r => r.IsHidden == true);
                else if (status == "visible") query = query.Where(r => r.IsHidden == false);
            }

            var reviews = await query
                .OrderBy(r => r.IsRead)
                .ThenByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    reviewId = r.ReviewId,
                    customerName = r.Customer != null ? r.Customer.FullName : "Khách vãng lai",
                    customerPhone = r.Customer != null ? r.Customer.Phone : "Không có SĐT",
                    orderId = r.OrderId,
                    rating = r.Rating ?? 5,
                    comment = r.Comment ?? string.Empty,
                    createdAt = r.CreatedAt ?? DateTime.Now,
                    isRead = r.IsRead,
                    isHidden = r.IsHidden,
                    adminReply = r.AdminReply,

                    // Trích xuất thông tin cấu hình biến thể di động (RAM/Storage/Màu sắc) của thiết bị khách đã mua
                    variantSpec = r.ReviewDetails.FirstOrDefault() != null && r.ReviewDetails.FirstOrDefault()!.Variant != null
                        ? (r.ReviewDetails.FirstOrDefault()!.Variant.Ram + " " + r.ReviewDetails.FirstOrDefault()!.Variant.Storage + " - " + r.ReviewDetails.FirstOrDefault()!.Variant.Color).Trim(' ', '-')
                        : "",

                    // Trích xuất chuỗi hình ảnh/video chứng từ thực tế của khách hàng dạng Shopee style
                    mediaUrls = r.ReviewDetails.FirstOrDefault() != null ? r.ReviewDetails.FirstOrDefault()!.MediaUrls : ""
                })
                .ToListAsync();

            return Json(reviews);
        }

        // 3. API AJAX: Đánh dấu đã đọc khi admin click xem chi tiết
        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int reviewId)
        {
            var review = await _context.Reviews.FindAsync(reviewId);
            if (review == null) return NotFound();

            review.IsRead = true;
            await _context.SaveChangesAsync();
            return Ok();
        }

        // 4. API AJAX: Ẩn/Hiện đánh giá (Soft Delete chuẩn sàn thương mại)
        [HttpPost]
        public async Task<IActionResult> ToggleHide(int reviewId)
        {
            var review = await _context.Reviews.FindAsync(reviewId);
            if (review == null) return NotFound();

            review.IsHidden = !review.IsHidden;
            await _context.SaveChangesAsync();
            return Json(new { success = true, isHidden = review.IsHidden });
        }

        // 5. API AJAX: Lưu lời phản hồi từ phía Admin cửa hàng
        [HttpPost]
        public async Task<IActionResult> ReplyReview(int reviewId, string replyText)
        {
            var review = await _context.Reviews.FindAsync(reviewId);
            if (review == null) return NotFound();

            review.AdminReply = replyText;
            review.IsRead = true;
            await _context.SaveChangesAsync();
            return Json(new { success = true, reply = replyText });
        }

        // 6. API AJAX: Cung cấp dữ liệu ngầm cho Real-time Polling cập nhật Sidebar
        [HttpGet]
        public async Task<IActionResult> GetSidebarUpdates()
        {
            var productGroups = await _context.Reviews
                .Where(r => r.ProductId != null)
                .GroupBy(r => r.ProductId)
                .Select(g => new ReviewGroupVM
                {
                    ProductId = g.Key!.Value,
                    ProductName = g.First().Product != null ? g.First().Product!.Name : "Sản phẩm ẩn",
                    UnreadCount = g.Count(r => !r.IsRead),
                    TotalCount = g.Count(),
                    LatestReviewDate = g.Max(r => r.CreatedAt) ?? DateTime.Now
                })
                .OrderByDescending(g => g.LatestReviewDate)
                .ToListAsync();

            return Json(productGroups);
        }
    }
}