using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CustomerController : Controller
    {
        private readonly ApplicationDbContext _context;
        public CustomerController(ApplicationDbContext context) => _context = context;

        // 1. Màn hình danh sách kết hợp bộ lọc đa chiều & tìm kiếm tương đối
        public async Task<IActionResult> Index(string searchKeyword, string customerType, string status)
        {
            // Lưu lại bộ lọc cũ để hiển thị trên giao diện View
            ViewBag.SearchKeyword = searchKeyword;
            ViewBag.SelectedType = customerType;
            ViewBag.SelectedStatus = status;

            // Khởi tạo truy vấn động tối ưu hiệu năng
            var query = _context.Customer
                .Include(c => c.Account)
                .Include(c => c.Orders)
                .AsQueryable();

            // Lọc 1: Tìm kiếm tương đối theo Tên, Số điện thoại hoặc Email tài khoản
            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                var kw = searchKeyword.Trim().ToLower();
                query = query.Where(c => c.FullName.ToLower().Contains(kw) ||
                                         (c.Phone != null && c.Phone.Contains(kw)) ||
                                         c.Account.Email.ToLower().Contains(kw));
            }

            // Lọc 2: Lọc đa chiều theo phân khúc hạng thành viên
            if (!string.IsNullOrEmpty(customerType))
            {
                query = query.Where(c => c.CustomerType == customerType);
            }

            // Lọc 3: Lọc đa chiều theo trạng thái hoạt động của tài khoản (Active/Banned)
            if (!string.IsNullOrEmpty(status))
            {
                bool isActiveFilter = status == "Active";
                query = query.Where(c => c.Account.IsActive == isActiveFilter);
            }

            // Ép kiểu chuyển đổi sang ViewModel một lần duy nhất tại tầng database
            var customers = await query.Select(c => new CustomerListItemVM
            {
                CustomerId = c.CustomerId,
                AccountId = c.AccountId,
                FullName = c.FullName,
                Email = c.Account.Email,
                Phone = c.Phone ?? c.Account.Phone ?? "Chưa cập nhật",
                CustomerType = c.CustomerType ?? "Newbie",
                IsActive = c.Account.IsActive ?? true,
                TotalOrders = c.Orders.Count(),
                // Tính tổng chi tiêu thực tế dựa trên các đơn hàng đã giao dịch thành công hoàn toàn
                TotalSpent = c.Orders.Where(o => o.Status == "Hoàn thành").Sum(o => o.TotalAmount ?? 0),
                CreatedAt = c.Account.CreatedAt ?? DateTime.Now
            })
            .OrderByDescending(c => c.TotalSpent) // Quy chuẩn kinh doanh: Ưu tiên hiển thị những "Khách VIP chi đậm" lên trước
            .ToListAsync();

            return View(customers);
        }

        // 2. API AJAX: Đảo trạng thái tài khoản (Khóa khẩn cấp / Mở khóa) chuẩn Shopee
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(int accountId)
        {
            var account = await _context.Account.FindAsync(accountId);
            if (account == null) return Json(new { success = false, message = "Không tìm thấy thông tin tài khoản." });

            account.IsActive = !(account.IsActive ?? true); // Đảo ngược trạng thái hoạt động hiện tại
            await _context.SaveChangesAsync();

            return Json(new { success = true, isActive = account.IsActive });
        }

        // 3. API AJAX: Thay đổi nhanh phân hạng thành viên của khách hàng trực tiếp trên lưới dữ liệu
        [HttpPost]
        public async Task<IActionResult> UpdateTier(int customerId, string newTier)
        {
            var customer = await _context.Customer.FindAsync(customerId);
            if (customer == null) return Json(new { success = false, message = "Không tìm thấy thông tin khách hàng." });

            customer.CustomerType = newTier;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // 4. Màn hình chi tiết khách hàng (Customer 360 View)
        public async Task<IActionResult> Details(int id)
        {
            var customer = await _context.Customer
                .Include(c => c.Account)
                .Include(c => c.Address)
                .Include(c => c.Orders)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer == null) return NotFound();

            // Thống kê nhanh để hiển thị lên thẻ báo cáo nội bộ
            ViewBag.TotalSpent = customer.Orders.Where(o => o.Status == "Hoàn thành").Sum(o => o.TotalAmount ?? 0);
            ViewBag.TotalOrders = customer.Orders.Count;

            return View(customer);
        }

        // 5. API AJAX: Cập nhật phân hạng (Chuyển luồng từ Index sang Details)
        [HttpPost]
        public async Task<IActionResult> UpdateTierFromDetails(int customerId, string newTier)
        {
            var customer = await _context.Customer.FindAsync(customerId);
            if (customer == null) return Json(new { success = false, message = "Không tìm thấy thông tin khách hàng." });

            customer.CustomerType = newTier;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // 6. API AJAX: Cấp lại mật khẩu tạm thời chuẩn quy chuẩn vận hành sàn thương mại
        [HttpPost]
        public async Task<IActionResult> ResetCustomerPassword(int accountId)
        {
            var account = await _context.Account.FindAsync(accountId);
            if (account == null) return Json(new { success = false, message = "Không tìm thấy thông tin tài khoản bảo mật." });

            // Quy chuẩn sàn: Tạo chuỗi mật khẩu tạm thời ngẫu nhiên có độ bảo mật cao
            // Khách hàng sẽ dùng mật khẩu này để đăng nhập và hệ thống sẽ bắt buộc đổi mật khẩu ở lần đầu tiên truy cập
            string uniqueStamp = Guid.NewGuid().ToString().Substring(0, 8);
            string temporaryPassword = $"Pst@{uniqueStamp}";

            // Gán mật khẩu mới vào thực thể tài khoản
            // Lưu ý: Trong môi trường chạy thật, đoạn này bạn hãy đi qua hàm băm mật khẩu (VD: BCrypt hoặc SHA256) của hệ thống
            account.Password = temporaryPassword;

            await _context.SaveChangesAsync();

            // Trả mật khẩu tạm thời về giao diện Admin để nhân viên hỗ trợ sao chép gửi cho khách hàng
            return Json(new { success = true, tempPassword = temporaryPassword });
        }
    }
}