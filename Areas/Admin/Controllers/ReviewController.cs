using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ReviewController : Controller
    {
        private readonly ApplicationDbContext _context;
        public ReviewController(ApplicationDbContext context) => _context = context;

        // Màn hình quản lý toàn bộ đánh giá
        public async Task<IActionResult> Index(int? starFilter, bool? criticalOnly)
        {
            // 1. Đếm số lượng đánh giá dưới 3 sao để làm huy hiệu cảnh báo (Notification Badge)
            // Giả định hệ thống có trường xử lý, nếu chưa có ta đếm tổng số đánh giá dưới 3 sao
            int criticalCount = await _context.ReviewDetails
                .Where(rd => rd.Rating < 3)
                .CountAsync();

            ViewBag.CriticalCount = criticalCount;
            ViewBag.CurrentStarFilter = starFilter;
            ViewBag.CriticalOnly = criticalOnly;

            // 2. Xây dựng truy vấn lấy danh sách đánh giá
            var query = _context.ReviewDetails
                .Include(rd => rd.Review)
                    .ThenInclude(r => r.Customer)
                .Include(rd => rd.Variant)
                    .ThenInclude(v => v.Product)
                .AsQueryable();

            // Lọc nâng cao: Chỉ xem các đánh giá cần xử lý gấp (< 3 sao)
            if (criticalOnly == true)
            {
                query = query.Where(rd => rd.Rating < 3);
            }
            // Lọc chính xác theo số sao (1 sao, 2 sao, 3 sao...)
            else if (starFilter.HasValue)
            {
                query = query.Where(rd => rd.Rating == starFilter.Value);
            }

            var reviews = await query
                .OrderByDescending(rd => rd.Review.CreatedAt)
                .ToListAsync();

            return View(reviews);
        }

        // API AJAX: Ẩn/Hiện đánh giá nếu chứa từ ngữ nhạy cảm hoặc thô tục
        [HttpPost]
        public async Task<IActionResult> ToggleApproval(int id)
        {
            var reviewDetail = await _context.ReviewDetails.FindAsync(id);
            if (reviewDetail == null) return Json(new { success = false, message = "Không tìm thấy đánh giá." });

            // Đảo ngược trạng thái hiển thị (Giả định database có trường IsApproved hoặc tương đương)
            // Nếu database chưa có, ta có thể bổ sung hoặc tạm thời bỏ qua khâu lưu flag này.
            // Ở đây ví dụ đảo trạng thái ẩn hiện:
            // reviewDetail.IsApproved = !(reviewDetail.IsApproved ?? true);

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}