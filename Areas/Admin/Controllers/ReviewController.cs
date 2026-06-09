using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
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
                    TotalCount = g.Count(), // Khớp với thuộc tính vừa cập nhật
                    LatestReviewDate = g.Max(r => r.CreatedAt) ?? DateTime.Now
                })
                .OrderByDescending(g => g.LatestReviewDate) // Thuật toán: Đẩy sản phẩm có tương tác mới nhất lên đầu
                .ToListAsync();

            return View(productGroups);
        }

        [HttpGet]
        public async Task<IActionResult> GetReviewsByProduct(int productId)
        {
            var reviews = await _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.Order) // Nối bảng Đơn hàng để xác thực Verified Purchase
                .Where(r => r.ProductId == productId)
                .OrderBy(r => r.IsRead) // Đẩy bình luận chưa đọc lên đầu
                .ThenByDescending(r => r.CreatedAt) // Xếp theo thời gian mới nhất
                .Select(r => new ProductReviewDetailVM
                {
                    ReviewId = r.ReviewId,

                    // Đã mapping chính xác với thuộc tính FullName trong Customer.cs
                    CustomerName = r.Customer != null ? r.Customer.FullName : "Khách vãng lai",

                    // Đã mapping chính xác với thuộc tính Phone trong Customer.cs
                    CustomerPhone = r.Customer != null ? r.Customer.Phone : "Không có SĐT",

                    OrderId = r.OrderId,
                    Rating = r.Rating ?? 5,
                    Comment = r.Comment ?? string.Empty,
                    CreatedAt = r.CreatedAt ?? DateTime.Now,
                    IsRead = r.IsRead,
                    IsHidden = r.IsHidden,
                    AdminReply = r.AdminReply
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

            review.IsHidden = !review.IsHidden; // Đảo trạng thái ẩn/hiện
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
            review.IsRead = true; // Phản hồi đồng nghĩa với việc đã đọc
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
                .OrderByDescending(g => g.LatestReviewDate) // Luôn đẩy sản phẩm có biến động mới nhất lên đầu
                .ToListAsync();

            return Json(productGroups);
        }
    }
}