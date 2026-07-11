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
        private readonly IOrderInventoryService _orderInventoryService;

        public PaymentController(
            ApplicationDbContext context,
            VnPayService vnPayService,
            IOrderInventoryService orderInventoryService)
        {
            _context = context;
            _vnPayService = vnPayService;
            _orderInventoryService = orderInventoryService;
        }

        // =================================================================
        // 1. CALLBACK ĐỒNG BỘ (Trình duyệt khách hàng nhận kết quả)
        // =================================================================
        [HttpGet]
        public async Task<IActionResult> PaymentReturn()
        {
            var response = _vnPayService.PaymentExecute(Request.Query);

            if (response == null || !response.Success)
            {
                TempData["Error"] = "Thanh toán thất bại hoặc giao dịch bị hủy bỏ.";
                return RedirectToAction("Index", "Cart");
            }

            int orderId = Convert.ToInt32(response.OrderId);

            // Giữ hành vi hiện tại để không trộn payment lifecycle vào batch tồn kho.
            // Phase VNPay sau sẽ dùng IPN làm nguồn xác nhận thanh toán duy nhất.
            var order = await _context.Orders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order != null)
            {
                var payment = order.Payments.FirstOrDefault();

                if (payment != null &&
                    payment.PaymentStatus != "Đã thanh toán" &&
                    payment.PaymentStatus != "Đã hoàn tiền")
                {
                    order.Status = "Đang xử lý";
                    payment.PaymentStatus = "Đã thanh toán";
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

            TempData["OrderSuccessModal"] = JsonSerializer.Serialize(
                new { OrderId = orderId, Method = "VNPAY" });

            return RedirectToAction("Orders", "Customer");
        }

        // =================================================================
        // 2. IPN/WEBHOOK BẢO MẬT (Server-to-Server)
        // =================================================================
        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> BankIPN()
        {
            var parameters = Request.Method == "POST"
                ? Request.Form.ToDictionary(x => x.Key, x => x.Value.ToString())
                : Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());

            var ipnResponse = _vnPayService.ProcessIPN(parameters);

            if (!ipnResponse.IsValidChecksum)
            {
                return Json(new { RspCode = "97", Message = "Invalid Checksum" });
            }

            var order = await _context.Orders
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
            if (payment == null)
            {
                return Json(new { RspCode = "99", Message = "Payment Info Missing" });
            }

            if (payment.PaymentStatus == "Đã thanh toán" ||
                payment.PaymentStatus == "Đã hoàn tiền")
            {
                return Json(new { RspCode = "02", Message = "Order already confirmed" });
            }

            if (payment.PaymentStatus == "Thất bại" && order.Status == "Đã hủy")
            {
                return Json(new { RspCode = "00", Message = "Failure already processed" });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                if (ipnResponse.TransactionStatus == "00")
                {
                    order.Status = "Đang xử lý";
                    payment.PaymentStatus = "Đã thanh toán";
                    payment.PaymentDate = DateTime.Now;

                    _context.OrderHistories.Add(new OrderHistory
                    {
                        OrderId = order.OrderId,
                        Status = "Đang xử lý",
                        UpdatedAt = DateTime.Now,
                        Note = $"Ngân hàng xác nhận đã thu hộ {ipnResponse.Amount:N0}đ. Mã GD: {ipnResponse.TransactionId}."
                    });
                }
                else if (ipnResponse.TransactionStatus == "02" ||
                         ipnResponse.TransactionStatus == "99")
                {
                    var restored = await _orderInventoryService.RestoreOrderStockAsync(
                        order.OrderId,
                        "Hoàn kho do VNPAY báo giao dịch thất bại",
                        restoreFlashSaleSlots: true,
                        occurredAt: DateTime.Now,
                        cancellationToken: HttpContext.RequestAborted);

                    if (!restored)
                    {
                        // Một request đồng thời khác đã claim và hoàn kho trước.
                        // Reload để không ghi timeline/trạng thái trùng từ dữ liệu tracking cũ.
                        await _context.Entry(order).ReloadAsync(HttpContext.RequestAborted);
                        await _context.Entry(payment).ReloadAsync(HttpContext.RequestAborted);
                        await transaction.CommitAsync();

                        return Json(new
                        {
                            RspCode = "00",
                            Message = "Failure already processed"
                        });
                    }

                    order.Status = "Đã hủy";
                    payment.PaymentStatus = "Thất bại";

                    _context.OrderHistories.Add(new OrderHistory
                    {
                        OrderId = order.OrderId,
                        Status = "Đã hủy",
                        UpdatedAt = DateTime.Now,
                        Note = "[Tự động Webhook] Giao dịch thất bại. Hệ thống đã hủy đơn, hoàn kho và hoàn suất Flash Sale đúng một lần."
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { RspCode = "00", Message = "Confirm Success" });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();

                // Không trả nội dung exception hoặc dữ liệu nội bộ cho provider.
                return Json(new { RspCode = "99", Message = "System Error" });
            }
        }
    }
}
