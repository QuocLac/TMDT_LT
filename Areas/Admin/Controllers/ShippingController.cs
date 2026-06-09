using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ShippingController : Controller
    {
        private readonly ApplicationDbContext _context;
        public ShippingController(ApplicationDbContext context) => _context = context;

        // 1. TRANG DANH SÁCH CHÍNH (INDEX)
        public async Task<IActionResult> Index()
        {
            var carriers = await _context.ShippingCarriers.OrderByDescending(c => c.CreatedAt).ToListAsync();
            return View(carriers);
        }

        // 2. XỬ LÝ TẠO MỚI CỔNG VẬN CHUYỂN
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ShippingCarriers model)
        {
            if (ModelState.IsValid)
            {
                model.CreatedAt = DateTime.Now;
                model.IsActive = true;
                _context.ShippingCarriers.Add(model);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Tích hợp cổng vận chuyển mới thành công.";
                return RedirectToAction(nameof(Index));
            }
            TempData["Error"] = "Dữ liệu nhập vào không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        // 3. BẬT / TẮT TRẠNG THÁI HOẠT ĐỘNG (SOFT DELETE)
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var carrier = await _context.ShippingCarriers.FindAsync(id);
            if (carrier == null) return Json(new { success = false });

            carrier.IsActive = !carrier.IsActive;
            await _context.SaveChangesAsync();
            return Json(new { success = true, isActive = carrier.IsActive });
        }

        // 4. API LƯU THÔNG TIN CHỈNH SỬA TỪ MODAL (ĐÃ NÂNG CẤP)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ShippingCarriers model)
        {
            if (id != model.CarrierId) return Json(new { success = false, message = "Định danh mã cổng vận chuyển không đồng nhất." });

            if (!ModelState.IsValid) return Json(new { success = false, message = "Thông tin nhập vào không đáp ứng chuẩn định dạng kỹ thuật." });

            try
            {
                var carrier = await _context.ShippingCarriers.FindAsync(id);
                if (carrier == null) return Json(new { success = false, message = "Không tìm thấy dữ liệu đối tác vận chuyển trên hệ thống." });

                // Đồng bộ cập nhật các tham số cấu hình lõi
                carrier.CarrierName = model.CarrierName.Trim();
                carrier.ApiUrl = model.ApiUrl.Trim();
                carrier.ApiToken = model.ApiToken.Trim();

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi hệ thống trong quá trình ghi dữ liệu: " + ex.Message });
            }
        }
    }
}