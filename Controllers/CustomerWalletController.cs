using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // =======================================================
        // 1. TRANG "VÍ VOUCHER CỦA TÔI" (TRUNG TÂM TÀI KHOẢN)
        // Yêu cầu: Bắt buộc phải đăng nhập mới được xem ví cá nhân
        // =======================================================
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Kiểm tra trạng thái đăng nhập, nếu chưa thì đẩy về trang Login
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/CustomerWallet" });
            }

            var account = await _context.Account
                .Include(a => a.Customer)
                .FirstOrDefaultAsync(a => a.Email == User.Identity.Name);

            var customer = account?.Customer?.FirstOrDefault();
            if (customer == null)
            {
                return RedirectToAction("Login", "Account", new { returnUrl = "/CustomerWallet" });
            }

            // Lấy danh sách Voucher trong ví khách hàng
            var myWallets = await _context.CustomerWallet
                .Include(w => w.Promotion)
                    .ThenInclude(p => p.PromotionRules)
                .Where(w => w.CustomerId == customer.CustomerId)
                .OrderByDescending(w => w.SavedAt)
                .ToListAsync();

            // Cập nhật động trạng thái hết hạn
            var now = DateTime.Now;
            bool hasChanges = false;

            foreach (var item in myWallets.Where(w => w.Status == 0 && w.Promotion != null))
            {
                if (item.Promotion!.EndDate < now)
                {
                    item.Status = 2; // 2 = Đã hết hạn
                    _context.Entry(item).State = EntityState.Modified;
                    hasChanges = true;
                }
            }

            if (hasChanges) await _context.SaveChangesAsync();

            return View(myWallets);
        }

        // =======================================================
        // 2. KHO VOUCHER CÔNG KHAI (MỒI NHỬ TIẾP THỊ)
        // Yêu cầu: Ai cũng vào được (Không cần đăng nhập)
        // =======================================================
        [HttpGet]
        [AllowAnonymous] // Thẻ này giúp xuyên qua các bộ lọc khóa bảo mật nếu có
        public async Task<IActionResult> Public()
        {
            var now = DateTime.Now;

            // 1. Lấy toàn bộ các mã Công khai (TargetAudience = 0), còn hạn và còn lượt dùng
            var publicVouchers = await _context.Promotions
                .Include(p => p.PromotionRules)
                .Where(p => p.IsActive
                         && p.TargetAudience == 0
                         && p.StartDate <= now
                         && p.EndDate >= now
                         && p.UsedCount < p.UsageLimit)
                .OrderByDescending(p => p.StartDate)
                .ToListAsync();

            // 2. Kiểm tra ngầm: NẾU khách ĐÃ ĐĂNG NHẬP, lấy danh sách mã họ đã lưu để View hiển thị chữ "ĐÃ LƯU"
            // NẾU CHƯA ĐĂNG NHẬP, list này sẽ rỗng và mọi nút đều hiện chữ "LƯU MÃ"
            var savedVoucherIds = new List<int>();

            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var account = await _context.Account
                    .Include(a => a.Customer)
                    .FirstOrDefaultAsync(a => a.Email == User.Identity.Name);

                var customer = account?.Customer?.FirstOrDefault();
                if (customer != null)
                {
                    savedVoucherIds = await _context.CustomerWallet
                        .Where(w => w.CustomerId == customer.CustomerId)
                        .Select(w => w.PromotionId)
                        .ToListAsync();
                }
            }

            ViewBag.SavedVoucherIds = savedVoucherIds;

            return View(publicVouchers);
        }

        // =======================================================
        // 3. API THU THẬP VOUCHER VÀO VÍ (AJAX ENDPOINT)
        // Yêu cầu: Không khóa redirect, tự C# kiểm tra và ném lỗi JSON
        // =======================================================
        [HttpPost]
        [AllowAnonymous] // Phải để AllowAnonymous để AJAX không bị ép văng sang 302 Redirect gây lỗi 405
        public async Task<IActionResult> SaveVoucher(int promotionId)
        {
            // 1. RÀO CHẮN ĐĂNG NHẬP: Bắt buộc đăng nhập mới được LƯU MÃ
            if (User.Identity == null || !User.Identity.IsAuthenticated)
            {
                // Trả về JSON chứa cờ requireLogin = true để giao diện JS tự chuyển hướng
                return Json(new { success = false, message = "Vui lòng đăng nhập tài khoản để thu thập mã ưu đãi này.", requireLogin = true });
            }

            // 2. Xác thực tài khoản hợp lệ
            var account = await _context.Account
                .Include(a => a.Customer)
                .FirstOrDefaultAsync(a => a.Email == User.Identity.Name);

            var customer = account?.Customer?.FirstOrDefault();
            if (customer == null)
            {
                return Json(new { success = false, message = "Lỗi xác thực thông tin hội viên." });
            }

            // 3. Kiểm tra tính hợp lệ của Voucher
            var promo = await _context.Promotions.FindAsync(promotionId);
            if (promo == null || !promo.IsActive || promo.EndDate < DateTime.Now)
            {
                return Json(new { success = false, message = "Rất tiếc, chương trình ưu đãi này đã kết thúc." });
            }

            if (promo.UsedCount >= promo.UsageLimit)
            {
                return Json(new { success = false, message = "Mã giảm giá này đã được thu thập hết số lượng phát hành." });
            }

            // 4. Kiểm tra chống spam (Khách đã lưu chưa)
            bool isAlreadySaved = await _context.CustomerWallet
                .AnyAsync(w => w.CustomerId == customer.CustomerId && w.PromotionId == promotionId);

            if (isAlreadySaved)
            {
                return Json(new { success = false, message = "Bạn đã sở hữu mã giảm giá này trong ví tài khoản rồi." });
            }

            // 5. Ghi nhận nạp mã vào ví khách hàng
            try
            {
                var newWalletItem = new CustomerWallet
                {
                    CustomerId = customer.CustomerId,
                    PromotionId = promo.PromotionId,
                    Status = 0, // 0 = Trạng thái: Đã lưu, sẵn sàng sử dụng
                    SavedAt = DateTime.Now,
                    UsedAt = null
                };

                _context.CustomerWallet.Add(newWalletItem);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Thu thập voucher thành công! Mã đã được lưu vào ví của bạn." });
            }
            catch (Exception)
            {
                return Json(new { success = false, message = "Đã xảy ra lỗi trong quá trình lưu trữ. Vui lòng thử lại sau." });
            }
        }
    }
}