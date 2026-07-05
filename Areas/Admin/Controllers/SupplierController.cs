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
    public class SupplierController : Controller
    {
        private readonly ApplicationDbContext _context;
        public SupplierController(ApplicationDbContext context) => _context = context;

        // 1. TRANG INDEX TỔNG HỢP SỔ ĐỐI TÁC
        public async Task<IActionResult> Index()
        {
            var suppliers = await _context.Suppliers.OrderByDescending(s => s.CreatedAt).ToListAsync();
            return View(suppliers);
        }

        // 2. XỬ LÝ KHAI BÁO THÊM ĐỐI TÁC MỚI
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Suppliers model,int type)
        {
            // Kiểm tra thủ công vì model sinh từ DB không có [Required]
            if (string.IsNullOrWhiteSpace(model.SupplierName) || string.IsNullOrWhiteSpace(model.Phone))
            {
                TempData["Error"] = "Tên nhà phân phối và số điện thoại không được để trống.";
                return RedirectToAction(nameof(Index));
            }
            model.Type = type; // Lưu loại (1 hoặc 2)
            model.SupplierName = model.SupplierName.Trim();
            model.Phone = model.Phone.Trim();
            model.CreatedAt = DateTime.Now;
            model.IsActive = true;

            _context.Suppliers.Add(model);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Khai báo thông tin đối tác cung ứng thành công.";
            return RedirectToAction(nameof(Index));
        }

        // 3. ĐIỀU CHỈNH TRẠNG THÁI HỢP TÁC (SOFT DELETE)
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return Json(new { success = false });

            // Xử lý đảo ngược giá trị nullable bool
            supplier.IsActive = !(supplier.IsActive ?? false);

            await _context.SaveChangesAsync();
            return Json(new { success = true, isActive = supplier.IsActive });
        }

        // 4. API LƯU CHỈNH SỬA THÔNG TIN ĐỐI TÁC TỪ MODAL
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Suppliers model)
        {
            if (id != model.SupplierId) return Json(new { success = false, message = "Định danh mã nhà phân phối không hợp lệ." });

            if (string.IsNullOrWhiteSpace(model.SupplierName) || string.IsNullOrWhiteSpace(model.Phone))
                return Json(new { success = false, message = "Tên doanh nghiệp và số hotline không được bỏ trống." });

            try
            {
                var supplier = await _context.Suppliers.FindAsync(id);
                if (supplier == null) return Json(new { success = false, message = "Không tìm thấy dữ liệu đối tác." });

                // Cập nhật tất cả các trường bao gồm cả Mã số thuế
                supplier.Type = model.Type; // Cập nhật cả loại đối tác
                supplier.SupplierName = model.SupplierName.Trim();
                supplier.ContactName = model.ContactName?.Trim();
                supplier.Phone = model.Phone.Trim();
                supplier.Email = model.Email?.Trim();
                supplier.Address = model.Address?.Trim();
                supplier.TaxCode = model.TaxCode?.Trim();

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi ghi nhận dữ liệu máy chủ: " + ex.Message });
            }
        }
    }
}