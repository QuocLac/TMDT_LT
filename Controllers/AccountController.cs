using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models.ViewModels;

namespace TMDT_LT.Controllers
{
    [Authorize] // Bắt buộc đăng nhập
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. HIỂN THỊ TRANG THÔNG TIN TÀI KHOẢN
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            var customer = await _context.Customer
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Account.Email == email);

            if (customer == null) return NotFound();

            var model = new ProfileVM
            {
                Email = customer.Account.Email,
                FullName = customer.FullName,
                Phone = customer.Phone,
                Gender = customer.Gender,
                BirthDate = customer.BirthDate,
                CustomerType = customer.CustomerType,
                RewardPoints = customer.RewardPoints,
                CreatedAt = customer.Account.CreatedAt
            };

            return View(model);
        }

        // 2. CẬP NHẬT THÔNG TIN TÀI KHOẢN
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(ProfileVM model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Vui lòng kiểm tra lại dữ liệu nhập vào.";
                return View("Profile", model);
            }

            var email = User.FindFirstValue(ClaimTypes.Email);
            var customer = await _context.Customer
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Account.Email == email);

            if (customer == null) return NotFound();

            // Cập nhật dữ liệu
            customer.FullName = model.FullName;
            customer.Phone = model.Phone;
            customer.Gender = model.Gender;
            customer.BirthDate = model.BirthDate;

            // Đồng bộ số điện thoại sang bảng Account
            customer.Account.Phone = model.Phone;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Cập nhật thông tin tài khoản thành công!";
            return RedirectToAction(nameof(Profile));
        }
    }
}