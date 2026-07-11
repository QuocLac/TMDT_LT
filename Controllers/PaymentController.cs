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
using TMDT_LT.Services;

namespace TMDT_LT.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly VnPayService _vnPayService;
        private readonly IOrderInventoryService _orderInventoryService;
        private readonly IOrderStateService _orderStateService;

        public PaymentController(
            ApplicationDbContext context,
            VnPayService vnPayService,
            IOrderInventoryService orderInventoryService,
            IOrderStateService orderStateService)
        {
            _context = context;
            _vnPayService = vnPayService;
            _orderInventoryService = orderInventoryService;
            _orderStateService = orderStateService;
        }

        // Return URL chỉ phục vụ hiển thị. IPN mới là nguồn xác nhận dòng tiền.
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> PaymentReturn()
        {
            var response = _vnPayService.PaymentExecute(Request.Query);

            if (response == null || !response.Success || !int.TryParse(response.OrderId, out int orderId))
            {
                TempData["Error"] = "Thanh toán chưa thành công hoặc phản hồi từ VNPAY không hợp lệ.";
                return RedirectToAction("Orders", "Customer");
            }

            int customerId = int.TryParse(User.FindFirstValue("CustomerId"), out int parsedCustomerId)
                ? parsedCustomerId
                : 0;

            var order = await _context.Orders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

            if (order == null)
            {
                TempData["Error"] = "Không tìm thấy đơn hàng tương ứng.";
                return RedirectToAction("Orders", "Customer");
            }

            var payment = order.Payments.FirstOrDefault();
            bool confirmed = payment != null
                && (payment.PaymentStatus == PaymentStatuses.Paid
                    || payment.PaymentStatus == PaymentStatuses.Refunded);

            if (confirmed)
            {
                TempData["OrderSuccessModal"] = JsonSerializer.Serialize(
                    new { OrderId = orderId, Method = PaymentMethods.VnPay });
            }
            else
            {
                TempData["Error"] = "VNPAY đã trả khách về cửa hàng, nhưng hệ thống vẫn đang chờ IPN xác nhận giao dịch.";
            }

            return RedirectToAction("OrderPlaced", "Checkout", new { orderId });
        }

        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
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

            using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            try
            {
                var order = await _context.Orders
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.OrderId == ipnResponse.OrderId);

                if (order == null)
                {
                    await transaction.RollbackAsync();
                    return Json(new { RspCode = "01", Message = "Order Not Found" });
                }

                if (order.TotalAmount != ipnResponse.Amount)
                {
                    await transaction.RollbackAsync();
                    return Json(new { RspCode = "04", Message = "Invalid Amount" });
                }

                var payment = order.Payments.FirstOrDefault();
                if (payment == null)
                {
                    await transaction.RollbackAsync();
                    return Json(new { RspCode = "99", Message = "Payment Info Missing" });
                }

                if (!string.Equals(payment.PaymentMethod, PaymentMethods.VnPay, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return Json(new { RspCode = "02", Message = "Payment Method Mismatch" });
                }

                if (ipnResponse.TransactionStatus == "00")
                {
                    if (payment.PaymentStatus == PaymentStatuses.Paid
                        || payment.PaymentStatus == PaymentStatuses.Refunded)
                    {
                        await transaction.CommitAsync();
                        return Json(new { RspCode = "00", Message = "Already Confirmed" });
                    }

                    if (order.Status == OrderStatuses.Cancelled
                        || order.Status == OrderStatuses.Returned)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { RspCode = "02", Message = "Order Closed" });
                    }

                    payment.PaymentStatus = PaymentStatuses.Paid;
                    payment.PaymentDate = DateTime.Now;

                    if (order.Status == OrderStatuses.Pending)
                    {
                        _orderStateService.Transition(
                            order,
                            OrderStatuses.Processing,
                            $"VNPAY IPN xác nhận thanh toán thành công. Mã GD: {ipnResponse.TransactionId}.",
                            DateTime.Now);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return Json(new { RspCode = "00", Message = "Confirm Success" });
                }

                if (ipnResponse.TransactionStatus == "02"
                    || ipnResponse.TransactionStatus == "99")
                {
                    if (payment.PaymentStatus == PaymentStatuses.Failed
                        && order.Status == OrderStatuses.Cancelled)
                    {
                        await transaction.CommitAsync();
                        return Json(new { RspCode = "00", Message = "Failure Already Processed" });
                    }

                    if (payment.PaymentStatus == PaymentStatuses.Paid
                        || payment.PaymentStatus == PaymentStatuses.Refunded)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { RspCode = "02", Message = "Paid Order Cannot Be Cancelled" });
                    }

                    if (order.Status != OrderStatuses.Pending
                        && order.Status != OrderStatuses.Processing)
                    {
                        await transaction.RollbackAsync();
                        return Json(new { RspCode = "02", Message = "Order State Conflict" });
                    }

                    await _orderInventoryService.RestoreOrderStockAsync(
                        order.OrderId,
                        "Hoàn kho do VNPAY báo giao dịch thất bại",
                        restoreFlashSaleSlots: true,
                        occurredAt: DateTime.Now,
                        cancellationToken: HttpContext.RequestAborted);

                    payment.PaymentStatus = PaymentStatuses.Failed;

                    _orderStateService.Transition(
                        order,
                        OrderStatuses.Cancelled,
                        $"VNPAY IPN báo giao dịch thất bại. Mã GD: {ipnResponse.TransactionId}.",
                        DateTime.Now);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return Json(new { RspCode = "00", Message = "Failure Processed" });
                }

                await transaction.CommitAsync();
                return Json(new { RspCode = "00", Message = "Non-final Status Ignored" });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return Json(new { RspCode = "99", Message = "System Error" });
            }
        }
    }
}
