using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CategoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        public CategoryController(ApplicationDbContext context) => _context = context;

        // Hiển thị danh sách + Tìm kiếm
        public async Task<IActionResult> Index(string search)
        {
            var query = _context.Categories.AsQueryable();
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c => c.CategoryName.Contains(search));
            }
            ViewBag.Search = search;
            return View(await query.ToListAsync());
        }

        // Lưu danh mục mới hoặc cập nhật
        [HttpPost]
        public async Task<IActionResult> Save(Categories model)
        {
            if (string.IsNullOrEmpty(model.CategoryName))
            {
                TempData["Error"] = "Tên danh mục không được trống.";
                return RedirectToAction(nameof(Index));
            }

            if (model.CategoryId == 0) // Thêm mới
            {
                model.IsActive = true;
                _context.Categories.Add(model);
                TempData["Success"] = "Thêm danh mục thành công.";
            }
            else // Cập nhật
            {
                var target = await _context.Categories.FindAsync(model.CategoryId);
                if (target != null)
                {
                    target.CategoryName = model.CategoryName;
                    target.Description = model.Description;
                    TempData["Success"] = "Cập nhật danh mục thành công.";
                }
            }
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // Bật tắt trạng thái bằng AJAX
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var cat = await _context.Categories.FindAsync(id);
            if (cat == null) return Json(new { success = false });

            cat.IsActive = !(cat.IsActive ?? false);
            await _context.SaveChangesAsync();
            return Json(new { success = true, isActive = cat.IsActive });
        }
    }
}