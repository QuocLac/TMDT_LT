using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels;
using BCryptNet = BCrypt.Net.BCrypt;

namespace TMDT_LT.Controllers
{
    [Authorize] // Bắt buộc đăng nhập mới được vào các hàm này
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1. HIỂN THỊ TRANG PROFILE
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var email = User.FindFirstValue(ClaimTypes.Email);
            var account = await _context.Account
                .Include(a => a.Customer)
                .ThenInclude(c => c.Address)
                .FirstOrDefaultAsync(a => a.Email == email);

            if (account == null) return RedirectToAction("Login", "Auth");

            var customer = account.Customer.FirstOrDefault();

            // ========================================================
            // TỰ ĐỘNG CHỮA LÀNH DỮ LIỆU: Kích hoạt nếu Account thiếu Customer
            // ========================================================
            if (customer == null)
            {
                customer = new Customer
                {
                    AccountId = account.AccountId,
                    FullName = account.Role == "Admin" ? "Quản trị viên" : "Thành viên PHONE.ST",
                    CustomerType = "Newbie",
                    RewardPoints = 0,
                    Phone = account.Phone
                };
                _context.Customer.Add(customer);
                await _context.SaveChangesAsync();
            }
            // ========================================================

            var model = new ProfileVM
            {
                Email = account.Email,
                FullName = customer.FullName ?? "",
                Phone = customer.Phone,
                Gender = customer.Gender,
                BirthDate = customer.BirthDate,
                CustomerType = customer.CustomerType ?? "Newbie",
                RewardPoints = customer.RewardPoints,
                CreatedAt = account.CreatedAt,
                ActiveTab = TempData["ActiveTab"]?.ToString() ?? "profile", // Nhớ tab cũ

                // Dùng (customer.Address ?? new List<Address>()) để tránh lỗi Null với Customer mới tạo
                Addresses = (customer.Address ?? new List<Address>()).Select(a => new AddressVM
                {
                    AddressId = a.AddressId,
                    City = a.City ?? "",
                    District = a.District ?? "",
                    Street = a.Street ?? "",
                    IsDefault = a.IsDefault ?? false
                }).OrderByDescending(a => a.IsDefault).ToList()
            };

            return View(model);
        }

        // ==========================================
        // 2. CẬP NHẬT HỒ SƠ
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(ProfileVM model)
        {
            TempData["ActiveTab"] = "profile";

            var email = User.FindFirstValue(ClaimTypes.Email);
            var customer = await _context.Customer.Include(c => c.Account).FirstOrDefaultAsync(c => c.Account.Email == email);

            if (customer != null)
            {
                customer.FullName = model.FullName;
                customer.Phone = model.Phone;
                customer.Gender = model.Gender;
                customer.BirthDate = model.BirthDate;

                // Đồng bộ SDT qua bảng Account nếu cần
                customer.Account.Phone = model.Phone;

                await _context.SaveChangesAsync();
                TempData["Success"] = "Cập nhật hồ sơ cá nhân thành công!";
            }
            else
            {
                TempData["Error"] = "Đã xảy ra lỗi, không tìm thấy thông tin khách hàng.";
            }

            return RedirectToAction(nameof(Profile));
        }

        // ==========================================
        // 3. ĐỔI MẬT KHẨU (DÙNG BCRYPT)
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ProfileVM model)
        {
            TempData["ActiveTab"] = "password";

            var email = User.FindFirstValue(ClaimTypes.Email);
            var account = await _context.Account.FirstOrDefaultAsync(a => a.Email == email);

            if (account == null) return RedirectToAction("Login", "Auth");

            // Kiểm tra mật khẩu cũ (Có tính tương thích ngược cho PlainText)
            bool isOldPasswordValid = false;
            if (account.Password.StartsWith("$2"))
            {
                try { isOldPasswordValid = BCryptNet.Verify(model.PasswordData.OldPassword, account.Password); }
                catch { isOldPasswordValid = false; }
            }
            else
            {
                isOldPasswordValid = (account.Password == model.PasswordData.OldPassword);
            }

            if (!isOldPasswordValid)
            {
                TempData["Error"] = "Mật khẩu hiện tại không chính xác.";
                return RedirectToAction(nameof(Profile));
            }

            // Lưu mật khẩu mới
            account.Password = BCryptNet.HashPassword(model.PasswordData.NewPassword);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Đổi mật khẩu thành công!";
            return RedirectToAction(nameof(Profile));
        }

        // ==========================================
        // 4. QUẢN LÝ ĐỊA CHỈ
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> AddAddress(ProfileVM model)
        {
            TempData["ActiveTab"] = "address";

            var email = User.FindFirstValue(ClaimTypes.Email);
            var customer = await _context.Customer.Include(c => c.Address).FirstOrDefaultAsync(c => c.Account.Email == email);
            if (customer == null) return NotFound();

            var newAddr = model.NewAddress;
            bool isFirstAddress = !customer.Address.Any();

            // Nếu đây là địa chỉ đầu tiên hoặc được tick mặc định, tắt các mặc định khác
            if (isFirstAddress || newAddr.IsDefault)
            {
                foreach (var addr in customer.Address) addr.IsDefault = false;
            }

            var address = new Address
            {
                CustomerId = customer.CustomerId,
                City = newAddr.City,
                District = newAddr.District,
                Street = newAddr.Street,
                Country = "Việt Nam",
                IsDefault = isFirstAddress ? true : newAddr.IsDefault
            };

            _context.Address.Add(address);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Thêm địa chỉ giao hàng thành công!";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAddress(int addressId)
        {
            TempData["ActiveTab"] = "address";
            var address = await _context.Address.FindAsync(addressId);

            if (address != null)
            {
                _context.Address.Remove(address);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Đã xóa địa chỉ.";
            }
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        public async Task<IActionResult> SetDefaultAddress(int addressId)
        {
            TempData["ActiveTab"] = "address";
            var email = User.FindFirstValue(ClaimTypes.Email);
            var customer = await _context.Customer.Include(c => c.Address).FirstOrDefaultAsync(c => c.Account.Email == email);

            if (customer != null)
            {
                foreach (var addr in customer.Address)
                {
                    addr.IsDefault = (addr.AddressId == addressId);
                }
                await _context.SaveChangesAsync();
                TempData["Success"] = "Đã thiết lập địa chỉ mặc định.";
            }
            return RedirectToAction(nameof(Profile));
        }
    }
}