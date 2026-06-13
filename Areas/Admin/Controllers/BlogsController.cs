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
    public class BlogsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BlogsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. DANH SÁCH BÀI VIẾT (INDEX)
        public async Task<IActionResult> Index()
        {
            var blogs = await _context.Blogs
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
            return View(blogs);
        }

        // 2. GIAO DIỆN TẠO MỚI (GET)
        public IActionResult Create()
        {
            return View();
        }

        // 3. XỬ LÝ LƯU TẠO MỚI (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Blog blog)
        {
            if (ModelState.IsValid)
            {
                blog.CreatedAt = DateTime.Now;
                if (blog.IsActive && blog.PublishedAt == null)
                {
                    blog.PublishedAt = DateTime.Now;
                }

                _context.Add(blog);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(blog);
        }

        // 4. GIAO DIỆN CHỈNH SỬA CẬP NHẬT (GET)
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var blog = await _context.Blogs.FindAsync(id);
            if (blog == null) return NotFound();

            return View(blog);
        }

        // 5. XỬ LÝ LƯU CẬP NHẬT (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Blog blog)
        {
            if (id != blog.BlogId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var existingBlog = await _context.Blogs.AsNoTracking().FirstOrDefaultAsync(b => b.BlogId == id);
                    if (existingBlog == null) return NotFound();

                    blog.CreatedAt = existingBlog.CreatedAt;

                    if (blog.IsActive && blog.PublishedAt == null)
                    {
                        blog.PublishedAt = DateTime.Now;
                    }

                    _context.Update(blog);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Blogs.Any(e => e.BlogId == blog.BlogId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(blog);
        }

        // 6. API XÓA BÀI VIẾT (AJAX POST)
        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var blog = await _context.Blogs
                .Include(b => b.BlogMedias)
                .FirstOrDefaultAsync(b => b.BlogId == id);

            if (blog == null)
            {
                return Json(new { success = false, message = "Bài viết không tồn tại hoặc đã bị xóa trước đó." });
            }

            try
            {
                if (blog.BlogMedias != null && blog.BlogMedias.Any())
                {
                    _context.BlogMedias.RemoveRange(blog.BlogMedias);
                }

                _context.Blogs.Remove(blog);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã xóa bài viết thành công khỏi hệ thống." });
            }
            catch (Exception)
            {
                return Json(new { success = false, message = "Đã xảy ra lỗi hệ thống trong quá trình xóa dữ liệu." });
            }
        }
    }
}