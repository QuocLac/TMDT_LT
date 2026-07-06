using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Controllers
{
    public class CustomerWalletController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CustomerWalletController(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<Customer?> GetCurrentCustomerAsync()
        {
            if (User.Identity?.IsAuthenticated != true) return null;

            var rawCustomerId = User.FindFirst("CustomerId")?.Value;
            if (int.TryParse(rawCustomerId, out int customerId) && customerId > 0)
            {
                return await _context.Customer
                    .Include(c => c.Account)
                    .FirstOrDefaultAsync(c => c.CustomerId == customerId);
            }

            var email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(email))
            {
                return await _context.Customer
                    .Include(c => c.Account)
                    .FirstOrDefaultAsync(c => c.Account != null && c.Account.Email == email);
            }

            return null;
        }

        // =======================================================
        // 1. TRANG "VÍ VOUCHER CỦA TÔI" (TRUNG TÂM TÀI KHOẢN)
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var customer = await GetCurrentCustomerAsync();
            if (customer == null)
            {
                return RedirectToAction("Login", "Auth", new { returnUrl = "/CustomerWallet" });
            }

            var myWallets = await _context.CustomerWallet
                .Include(w => w.Promotion)
                    .ThenInclude(p => p.PromotionRules)
                .Where(w => w.CustomerId == customer.CustomerId)
                .OrderByDescending(w => w.SavedAt)
                .ToListAsync();

            var now = DateTime.Now;
            bool hasChanges = false;

            foreach (var item in myWallets.Where(w => w.Status == 0 && w.Promotion != null && w.Promotion.EndDate < now))
            {
                item.Status = 2; // 2 = Đã hết hạn
                hasChanges = true;
            }

            if (hasChanges) await _context.SaveChangesAsync();

            return View(myWallets);
        }

        // =======================================================
        // 2. KHO VOUCHER CÔNG KHAI
        // =======================================================
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Public()
        {
            var now = DateTime.Now;

            var publicVouchers = await _context.Promotions
                .Include(p => p.PromotionRules)
                .Where(p => p.IsActive
                         && p.TargetAudience == 0
                         && p.StartDate <= now
                         && p.EndDate >= now
                         && p.UsedCount < p.UsageLimit)
                .OrderByDescending(p => p.StartDate)
                .ToListAsync();

            var savedVoucherIds = new List<int>();
            var customer = await GetCurrentCustomerAsync();
            if (customer != null)
            {
                savedVoucherIds = await _context.CustomerWallet
                    .Where(w => w.CustomerId == customer.CustomerId)
                    .Select(w => w.PromotionId)
                    .ToListAsync();
            }

            ViewBag.SavedVoucherIds = savedVoucherIds;
            return View(publicVouchers);
        }

        // =======================================================
        // 3. API THU THẬP VOUCHER VÀO VÍ (AJAX ENDPOINT)
        // =======================================================
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> SaveVoucher(int promotionId)
        {
            var customer = await GetCurrentCustomerAsync();
            if (customer == null)
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập tài khoản để lưu mã ưu đãi này.", requireLogin = true });
            }

            var now = DateTime.Now;
            var promo = await _context.Promotions
                .Include(p => p.PromotionRules)
                .FirstOrDefaultAsync(p => p.PromotionId == promotionId);

            if (promo == null || !promo.IsActive || promo.StartDate > now || promo.EndDate < now)
            {
                return Json(new { success = false, message = "Chương trình ưu đãi này chưa mở hoặc đã kết thúc." });
            }

            if (promo.UsedCount >= promo.UsageLimit)
            {
                return Json(new { success = false, message = "Mã giảm giá này đã hết lượt sử dụng." });
            }

            if (promo.TargetAudience != 0)
            {
                bool alreadyGranted = await _context.CustomerWallet
                    .AnyAsync(w => w.CustomerId == customer.CustomerId && w.PromotionId == promotionId);

                if (!alreadyGranted)
                {
                    return Json(new { success = false, message = "Mã này thuộc chương trình dành riêng cho một nhóm khách hàng." });
                }
            }

            bool isAlreadySaved = await _context.CustomerWallet
                .AnyAsync(w => w.CustomerId == customer.CustomerId && w.PromotionId == promotionId);

            if (isAlreadySaved)
            {
                return Json(new { success = false, message = "Bạn đã lưu mã này trong ví voucher rồi." });
            }

            try
            {
                _context.CustomerWallet.Add(new CustomerWallet
                {
                    CustomerId = customer.CustomerId,
                    PromotionId = promo.PromotionId,
                    Status = 0,
                    SavedAt = now,
                    UsedAt = null
                });

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Đã lưu voucher vào ví. Bạn có thể áp dụng ở giỏ hàng hoặc checkout." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Chưa thể lưu voucher. Chi tiết: " + (ex.InnerException?.Message ?? ex.Message) });
            }
        }
    }
}
