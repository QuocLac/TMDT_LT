using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly VnPayService _vnPayService;

        public PaymentController(ApplicationDbContext context, VnPayService vnPayService)
        {
            _context = context;
            _vnPayService = vnPayService;
        }

        // =================================================================
        // 1. CALLBACK ĐỒNG BỘ (Trình duyệt khách hàng nhận kết quả)
        // =================================================================
        [HttpGet]
        public async Task<IActionResult> PaymentReturn()
        {
            // Đọc kết quả trả về từ URL ngân hàng
            var response = _vnPayService.PaymentExecute(Request.Query);

            if (response == null || !response.Success)
            {
                TempData["Error"] = "Thanh toán thất bại hoặc giao dịch bị hủy bỏ.";
                return RedirectToAction("Index", "Cart");
            }

            int orderId = Convert.ToInt32(response.OrderId);

            // --- BỔ SUNG FIX LỖI LOCALHOST: CẬP NHẬT TRẠNG THÁI NGAY TẠI ĐÂY ---
            var order = await _context.Orders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order != null)
            {
                var payment = order.Payments.FirstOrDefault();

                // Chỉ cập nhật nếu nó chưa được IPN cập nhật trước đó
                if (payment != null && payment.PaymentStatus != "Đã thanh toán" && payment.PaymentStatus != "Đã hoàn tiền")
                {
                    order.Status = "Đang xử lý"; // Đẩy qua kho đóng gói
                    payment.PaymentStatus = "Đã thanh toán"; // Chốt dòng tiền
                    payment.PaymentDate = DateTime.Now;

                    _context.OrderHistories.Add(new OrderHistory
                    {
                        OrderId = order.OrderId,
                        Status = "Đang xử lý",
                        UpdatedAt = DateTime.Now,
                        Note = $"[Return URL] Xác nhận giao dịch VNPAY thành công. Mã GD: {response.TransactionId}."
                    });

                    await _context.SaveChangesAsync();
                }
            }
            // -----------------------------------------------------------------

            // Bật Modal chúc mừng bên trang quản lý đơn hàng
            TempData["OrderSuccessModal"] = JsonSerializer.Serialize(new { OrderId = orderId, Method = "VNPAY" });
            return RedirectToAction("Orders", "Customer");
        }

        // =================================================================
        // 2. IPN/WEBHOOK BẢO MẬT (Server-to-Server) - XỬ LÝ CHÍNH KHÓA DÒNG TIỀN
        // Ngân hàng sẽ gọi API này ngầm để đảm bảo dữ liệu không bị làm giả
        // =================================================================
        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> BankIPN()
        {
            // Giả lập đọc dữ liệu bảo mật từ Ngân hàng / Cổng thanh toán ném về
            var parameters = Request.Method == "POST" ? Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString()) : Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());

            var ipnResponse = _vnPayService.ProcessIPN(parameters);

            if (!ipnResponse.IsValidChecksum)
            {
                return Json(new { RspCode = "97", Message = "Invalid Checksum" }); // Lỗi chữ ký bảo mật
            }

            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderId == ipnResponse.OrderId);

            if (order == null)
            {
                return Json(new { RspCode = "01", Message = "Order Not Found" });
            }

            if (order.TotalAmount != ipnResponse.Amount)
            {
                return Json(new { RspCode = "04", Message = "Invalid Amount" });
            }

            var payment = order.Payments.FirstOrDefault();
            if (payment == null) return Json(new { RspCode = "99", Message = "Payment Info Missing" });

            if (payment.PaymentStatus == "Đã thanh toán" || payment.PaymentStatus == "Đã hoàn tiền")
            {
                return Json(new { RspCode = "02", Message = "Order already confirmed" });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // TRƯỜNG HỢP 1: NGÂN HÀNG BÁO KHÁCH THANH TOÁN THÀNH CÔNG
                if (ipnResponse.TransactionStatus == "00") // 00 là mã chuẩn giao dịch thành công
                {
                    order.Status = "Đang xử lý"; // Chuyển từ Chờ xác nhận sang Đang xử lý
                    payment.PaymentStatus = "Đã thanh toán";
                    payment.PaymentDate = DateTime.Now;

                    _context.OrderHistories.Add(new OrderHistory
                    {
                        OrderId = order.OrderId,
                        Status = "Đang xử lý",
                        UpdatedAt = DateTime.Now,
                        Note = $"Ngân hàng xác nhận đã thu hộ {string.Format("{0:N0}", ipnResponse.Amount)}đ. Mã GD: {ipnResponse.TransactionId}."
                    });
                }
                // TRƯỜNG HỢP 2: NGÂN HÀNG PHÁT TÍN HIỆU ĐẢO NGƯỢC/REVERSAL (HỦY/LỖI TỪ PHÍA NGÂN HÀNG)
                else if (ipnResponse.TransactionStatus == "02" || ipnResponse.TransactionStatus == "99")
                {
                    order.Status = "Đã hủy";
                    payment.PaymentStatus = "Thất bại";

                    // Đền bù hoàn trả lại kho ngay lập tức cho các biến thể sản phẩm
                    foreach (var detail in order.OrderDetails)
                    {
                        if (detail.Variant != null)
                        {
                            detail.Variant.Stock += detail.Quantity ?? 0;
                        }
                    }

                    _context.OrderHistories.Add(new OrderHistory
                    {
                        OrderId = order.OrderId,
                        Status = "Đã hủy",
                        UpdatedAt = DateTime.Now,
                        Note = $"[Tự động Webhook] Giao dịch thất bại từ phía ngân hàng. Hệ thống tự động hủy đơn và hoàn trả tồn kho."
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { RspCode = "00", Message = "Confirm Success" }); // Phản hồi chuẩn cho Ngân hàng dừng gửi tin
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { RspCode = "99", Message = "System Error: " + ex.Message });
            }
        }
    }
}