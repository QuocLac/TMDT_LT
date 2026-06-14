using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Controllers
{
    [Authorize] // Bắt buộc đăng nhập mới được vào khu vực quản lý tài khoản
    public class CustomerController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CustomerController(ApplicationDbContext context)
        {
            _context = context;
        }

        // =================================================================
        // 1. TRANG DANH SÁCH ĐƠN HÀNG (GET)
        // =================================================================
        [HttpGet]
        public async Task<IActionResult> Orders(string status = "TatCa")
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");

            // Khởi tạo truy vấn gốc lọc theo CustomerId
            var query = _context.Orders
                .Include(o => o.Payments)
                .Include(o => o.OrderDetails)
                .Where(o => o.CustomerId == customerId);

            // Bộ lọc tab trạng thái theo quy chuẩn sàn TMĐT
            if (status != "TatCa")
            {
                if (status == "YeuCauHuy")
                {
                    query = query.Where(o => o.Status == "Yêu cầu hủy");
                }
                else
                {
                    query = query.Where(o => o.Status == status);
                }
            }

            var orders = await query.OrderByDescending(o => o.OrderDate).ToListAsync();
            ViewBag.CurrentStatus = status;

            return View(orders);
        }

        // =================================================================
        // 2. API XỬ LÝ HỦY ĐƠN HÀNG (POST AJAX)
        // =================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestCancelOrder(int orderId, string reason)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");

            // Tải đơn hàng kèm chi tiết biến thể kho để sẵn sàng hoàn kho nếu cần
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

            if (order == null)
                return Json(new { success = false, message = "Không tìm thấy đơn hàng hợp lệ." });

            // KỊCH BẢN A: Đơn chờ xác nhận -> Hệ thống tự động duyệt hủy ngay lập tức
            if (order.Status == "Chờ xác nhận")
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    order.Status = "Đã hủy";
                    order.CancellationReason = reason;
                    order.CancellationRequestedBy = "Customer";

                    // Hoàn trả lại số lượng tồn kho cho các biến thể thiết bị
                    foreach (var detail in order.OrderDetails)
                    {
                        if (detail.Variant != null)
                        {
                            detail.Variant.Stock += detail.Quantity ?? 0;
                        }
                    }

                    // Cập nhật trạng thái thanh toán tương ứng
                    var payment = order.Payments.FirstOrDefault();
                    if (payment != null)
                    {
                        payment.PaymentStatus = payment.PaymentMethod == "COD" ? "Đã hủy" : "Chờ hoàn tiền";
                    }

                    // Ghi lại nhật ký vận hành hệ thống
                    _context.OrderHistories.Add(new OrderHistory
                    {
                        OrderId = order.OrderId,
                        Status = "Đã hủy",
                        UpdatedAt = DateTime.Now,
                        Note = $"[Tự động] Khách hàng chủ động hủy đơn thành công. Lý do: {reason}"
                    });

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Json(new { success = true, message = "Đơn hàng đã được hệ thống hủy tự động thành công." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
                }
            }

            // KỊCH BẢN B: Đơn đang xử lý hoặc chờ lấy hàng -> Chuyển trạng thái treo chờ Admin phê duyệt
            if (order.Status == "Đang xử lý" || order.Status == "Chờ lấy hàng")
            {
                order.Status = "Yêu cầu hủy";
                order.CancellationReason = reason;
                order.CancellationRequestedBy = "Customer";

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = "Yêu cầu hủy",
                    UpdatedAt = DateTime.Now,
                    Note = $"Khách hàng gửi yêu cầu xin hủy đơn. Lý do: {reason}. Chờ ban quản trị xét duyệt."
                });

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Đã gửi yêu cầu hủy đơn lên ban quản trị xét duyệt." });
            }

            return Json(new { success = false, message = "Đơn hàng đã bàn giao đơn vị vận chuyển, không thể thực hiện hủy." });
        }
    }
}