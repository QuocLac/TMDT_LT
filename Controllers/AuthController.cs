// Controllers/AuthController.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels;
using TMDT_LT.Models.ViewModels.Storefront;
using BCryptNet = BCrypt.Net.BCrypt;

namespace TMDT_LT.Controllers
{
    public class AuthController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuthController> _logger;

        public AuthController(ApplicationDbContext context, ILogger<AuthController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Vui lòng nhập email và mật khẩu.");
                return View();
            }

            var account = await _context.Account
                .Include(a => a.Customer)
                .FirstOrDefaultAsync(a => a.Email == email && a.IsActive == true);

            // ==========================================
            // FIX: XỬ LÝ LỖI MẬT KHẨU CŨ CHƯA ĐƯỢC HASH
            // ==========================================
            bool isPasswordValid = false;

            if (account != null)
            {
                // Mật khẩu hash bằng BCrypt luôn bắt đầu bằng "$2" (VD: $2a$, $2b$, $2y$)
                if (account.Password.StartsWith("$2"))
                {
                    try
                    {
                        isPasswordValid = BCryptNet.Verify(password, account.Password);
                    }
                    catch (BCrypt.Net.SaltParseException)
                    {
                        isPasswordValid = false;
                    }
                }
                else
                {
                    // Fallback: Dành cho các tài khoản cũ lưu bằng Plain Text (VD: "user789")
                    isPasswordValid = (password == account.Password);
                }
            }

            if (account == null || !isPasswordValid)
            {
                ModelState.AddModelError("", "Email hoặc mật khẩu không đúng.");
                return View();
            }
            // ==========================================

            var customerId = account.Customer.FirstOrDefault()?.CustomerId.ToString() ?? "";

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, account.AccountId.ToString()),
                new Claim(ClaimTypes.Email, account.Email),
                new Claim(ClaimTypes.Role, account.Role ?? "Customer"),
                new Claim("CustomerId", customerId)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTime.UtcNow.AddDays(7) });

            // BỔ SUNG: Gọi hàm gộp giỏ hàng vãng lai vào Database
            if (int.TryParse(customerId, out int cusIdParsed))
            {
                await MergeCartAfterLogin(cusIdParsed);
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return account.Role == "Admin"
                ? RedirectToAction("Index", "Dashboard", new { area = "Admin" })
                : RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string email, string password, string fullName, string phone)
        {
            if (await _context.Account.AnyAsync(a => a.Email == email))
            {
                ModelState.AddModelError("", "Email đã được đăng ký.");
                return View();
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var account = new Account
                {
                    Email = email,
                    Password = BCryptNet.HashPassword(password),
                    Role = "Customer",
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                    Phone = phone
                };
                _context.Account.Add(account);
                await _context.SaveChangesAsync();

                var customer = new Customer
                {
                    AccountId = account.AccountId,
                    FullName = fullName,
                    Phone = phone,
                    CustomerType = "Thường",
                    RewardPoints = 0
                };
                _context.Customer.Add(customer);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                TempData["Success"] = "Đăng ký thành công! Vui lòng đăng nhập.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Đăng ký thất bại cho email {Email}", email);
                ModelState.AddModelError("", $"Đăng ký thất bại: {ex.InnerException?.Message ?? ex.Message}");
                return View();
            }
        }

        // CẬP NHẬT: Hàm gộp giỏ hàng đọc từ Cookie thay vì Session
        private async Task MergeCartAfterLogin(int customerId)
        {
            var cartCookie = HttpContext.Request.Cookies["PhoneStCartCookie"];
            if (!string.IsNullOrEmpty(cartCookie))
            {
                try
                {
                    var guestCart = JsonSerializer.Deserialize<List<CartItemVM>>(cartCookie);
                    if (guestCart != null && guestCart.Any())
                    {
                        var dbCart = await _context.CartItems.Where(c => c.CustomerId == customerId).ToListAsync();

                        foreach (var item in guestCart)
                        {
                            var exist = dbCart.FirstOrDefault(c => c.VariantId == item.VariantId);
                            if (exist != null)
                            {
                                exist.Quantity += item.Quantity;
                            }
                            else
                            {
                                _context.CartItems.Add(new CartItems
                                {
                                    CustomerId = customerId,
                                    VariantId = item.VariantId,
                                    Quantity = item.Quantity,
                                    CreatedDate = DateTime.Now
                                });
                            }
                        }
                        await _context.SaveChangesAsync();
                    }
                }
                catch { }

                // Xóa Cookie sau khi đã gộp vào Database thành công
                HttpContext.Response.Cookies.Delete("PhoneStCartCookie");
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var account = await _context.Account.FirstOrDefaultAsync(a => a.Email == email && a.IsActive == true);
            if (account == null)
            {
                TempData["Error"] = "Email không tồn tại trong hệ thống.";
                return View();
            }

            var resetToken = Guid.NewGuid().ToString();
            HttpContext.Session.SetString("ResetToken_" + email, resetToken);
            HttpContext.Session.SetString("ResetTokenExpiry_" + email, DateTime.Now.AddHours(1).ToString());

            var resetLink = Url.Action("ResetPassword", "Auth", new { email = email, token = resetToken }, Request.Scheme);
            TempData["Success"] = $"Link đặt lại mật khẩu: {resetLink} (chỉ hiển thị trong dev)";
            return View();
        }

        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            var storedToken = HttpContext.Session.GetString("ResetToken_" + email);
            var expiryStr = HttpContext.Session.GetString("ResetTokenExpiry_" + email);

            if (string.IsNullOrEmpty(storedToken) || storedToken != token)
            {
                TempData["Error"] = "Link đặt lại mật khẩu không hợp lệ.";
                return RedirectToAction("ForgotPassword");
            }

            if (DateTime.TryParse(expiryStr, out var expiry) && expiry < DateTime.Now)
            {
                TempData["Error"] = "Link đã hết hạn.";
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string email, string token, string newPassword, string confirmPassword)
        {
            var storedToken = HttpContext.Session.GetString("ResetToken_" + email);
            if (string.IsNullOrEmpty(storedToken) || storedToken != token)
            {
                TempData["Error"] = "Link không hợp lệ.";
                return RedirectToAction("ForgotPassword");
            }

            if (newPassword != confirmPassword)
            {
                ModelState.AddModelError("", "Mật khẩu xác nhận không khớp.");
                ViewBag.Email = email;
                ViewBag.Token = token;
                return View();
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                ModelState.AddModelError("", "Mật khẩu phải có ít nhất 6 ký tự.");
                ViewBag.Email = email;
                ViewBag.Token = token;
                return View();
            }

            var account = await _context.Account.FirstOrDefaultAsync(a => a.Email == email);
            if (account == null) return NotFound();

            account.Password = BCryptNet.HashPassword(newPassword);
            await _context.SaveChangesAsync();

            HttpContext.Session.Remove("ResetToken_" + email);
            HttpContext.Session.Remove("ResetTokenExpiry_" + email);

            TempData["Success"] = "Mật khẩu đã được đặt lại. Vui lòng đăng nhập.";
            return RedirectToAction("Login");
        }
    }
}