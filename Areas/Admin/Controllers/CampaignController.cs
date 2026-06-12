using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CampaignController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public CampaignController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // 1. MÀN HÌNH CHÍNH: Lọc đa chiều & Tìm kiếm tương đối
        public async Task<IActionResult> Index(string searchKeyword, string status)
        {
            ViewBag.SearchKeyword = searchKeyword;
            ViewBag.SelectedStatus = status;

            var now = DateTime.Now;
            var query = _context.CampaignBanners.AsQueryable();

            // Tìm kiếm tương đối theo tiêu đề chiến dịch
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                query = query.Where(b => b.Title.ToLower().Contains(searchKeyword.Trim().ToLower()));
            }

            // Bộ lọc trạng thái tự động rẽ nhánh dựa vào mốc thời gian máy chủ
            if (!string.IsNullOrEmpty(status))
            {
                if (status == "Upcoming") query = query.Where(b => b.StartDate > now);
                else if (status == "Running") query = query.Where(b => b.StartDate <= now && b.EndDate >= now);
                else if (status == "Ended") query = query.Where(b => b.EndDate < now);
            }

            var banners = await query.OrderBy(b => b.DisplayOrder)
                                    .ThenByDescending(b => b.CreatedAt)
                                    .ToListAsync();
            return View(banners);
        }

        // 2. API AJAX: Lấy chi tiết thông tin nạp vào Modal (Xem/Sửa)
        [HttpGet]
        public async Task<IActionResult> GetDetail(int id)
        {
            var banner = await _context.CampaignBanners.FindAsync(id);
            if (banner == null) return NotFound();

            // Tính toán nhãn trạng thái thời gian thực gửi kèm ra giao diện
            string currentStatus = "Đã kết thúc";
            if (banner.StartDate > DateTime.Now) currentStatus = "Sắp diễn ra";
            else if (banner.StartDate <= DateTime.Now && banner.EndDate >= DateTime.Now) currentStatus = "Đang chạy";

            return Json(new
            {
                success = true,
                bannerId = banner.BannerId,
                title = banner.Title,
                imageUrl = banner.ImageUrl,
                targetUrl = banner.TargetUrl ?? "",
                startDate = banner.StartDate.ToString("yyyy-MM-ddTHH:mm"),
                endDate = banner.EndDate.ToString("yyyy-MM-ddTHH:mm"),
                displayOrder = banner.DisplayOrder,
                isActive = banner.IsActive,
                computedStatus = currentStatus
            });
        }

        // 3. XỬ LÝ LƯU (Hợp nhất hành động Thêm mới & Cập nhật sửa đổi)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(CampaignBanners model, IFormFile? uploadImage)
        {
            try
            {
                // Xử lý luồng tải tập tin hình ảnh nếu Admin đính kèm ảnh mới
                if (uploadImage != null && uploadImage.Length > 0)
                {
                    string folderPath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "banners");
                    if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                    string uniqueName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(uploadImage.FileName);
                    string fullPath = Path.Combine(folderPath, uniqueName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await uploadImage.CopyToAsync(stream);
                    }
                    model.ImageUrl = $"/uploads/banners/{uniqueName}";
                }

                if (model.BannerId == 0) // Trường hợp: THÊM MỚI SỰ KIỆN
                {
                    if (string.IsNullOrEmpty(model.ImageUrl))
                    {
                        TempData["Error"] = "Vui lòng đăng tải hình ảnh banner thiết kế tiếp thị.";
                        return RedirectToAction(nameof(Index));
                    }
                    _context.CampaignBanners.Add(model);
                    TempData["Success"] = "Khởi tạo chiến dịch tiếp thị thành công.";
                }
                else // Trường hợp: CHỈNH SỬA SỰ KIỆN ĐANG CÓ
                {
                    var existingBanner = await _context.CampaignBanners.FindAsync(model.BannerId);
                    if (existingBanner == null) return NotFound();

                    existingBanner.Title = model.Title;
                    existingBanner.TargetUrl = model.TargetUrl;
                    existingBanner.StartDate = model.StartDate;
                    existingBanner.EndDate = model.EndDate;
                    existingBanner.DisplayOrder = model.DisplayOrder;
                    existingBanner.IsActive = model.IsActive;

                    // Nếu không up ảnh mới, giữ lại đường dẫn ảnh cũ trong DB
                    if (!string.IsNullOrEmpty(model.ImageUrl))
                    {
                        existingBanner.ImageUrl = model.ImageUrl;
                    }

                    TempData["Success"] = "Cập nhật dữ liệu sự kiện hoàn tất.";
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi xử lý luồng ghi nhận dữ liệu: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // 4. API AJAX: Đảo trạng thái Ẩn/Hiện khẩn cấp trực tiếp trên lưới dữ liệu
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var banner = await _context.CampaignBanners.FindAsync(id);
            if (banner == null) return Json(new { success = false, message = "Không tìm thấy sự kiện." });

            banner.IsActive = !banner.IsActive;
            await _context.SaveChangesAsync();

            return Json(new { success = true, isActive = banner.IsActive });
        }
    }
}