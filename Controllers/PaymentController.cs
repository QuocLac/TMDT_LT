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
        private readonly IPaymentTransactionService _paymentTransactionService;
        private readonly PaymentExpirationPolicy _paymentExpirationPolicy;

        public PaymentController(
            ApplicationDbContext context,
            VnPayService vnPayService,
            IOrderInventoryService orderInventoryService,
            IOrderStateService orderStateService,
            IPaymentTransactionService paymentTransactionService,
            PaymentExpirationPolicy paymentExpirationPolicy)
        {
            _context = context;
            _vnPayService = vnPayService;
            _orderInventoryService = orderInventoryService;
            _orderStateService = orderStateService;
            _paymentTransactionService = paymentTransactionService;
            _paymentExpirationPolicy = paymentExpirationPolicy;
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


        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Status(int orderId)
        {
            int customerId = int.TryParse(User.FindFirstValue("CustomerId"), out int parsedCustomerId)
                ? parsedCustomerId
                : 0;

            var result = await _context.Payments
                .AsNoTracking()
                .Where(payment =>
                    payment.OrderId == orderId
                    && payment.Order != null
                    && payment.Order.CustomerId == customerId)
                .OrderByDescending(payment => payment.PaymentId)
                .Select(payment => new
                {
                    OrderId = payment.OrderId!.Value,
                    OrderStatus = payment.Order!.Status,
                    payment.PaymentStatus,
                    payment.PaymentMethod,
                    payment.CreatedAt,
                    payment.LastProcessedAt,
                    payment.LastResponseCode
                })
                .FirstOrDefaultAsync(HttpContext.RequestAborted);

            if (result == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy đơn hàng." });
            }

            var referenceTime = result.CreatedAt;
            if (string.Equals(result.LastResponseCode, "RETRY", StringComparison.Ordinal)
                && result.LastProcessedAt.HasValue
                && result.LastProcessedAt.Value > referenceTime)
            {
                referenceTime = result.LastProcessedAt.Value;
            }

            DateTime? expiresAt = _paymentExpirationPolicy.GetDeadline(
                result.PaymentMethod,
                referenceTime);

            return Json(new
            {
                success = true,
                orderId = result.OrderId,
                orderStatus = result.OrderStatus,
                paymentStatus = result.PaymentStatus,
                paymentMethod = result.PaymentMethod,
                expiresAt
            });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RetryVnPay(int orderId)
        {
            int customerId = int.TryParse(User.FindFirstValue("CustomerId"), out int parsedCustomerId)
                ? parsedCustomerId
                : 0;

            await using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                HttpContext.RequestAborted);

            try
            {
                var order = await _context.Orders
                    .Include(current => current.Payments)
                    .FirstOrDefaultAsync(
                        current => current.OrderId == orderId
                            && current.CustomerId == customerId,
                        HttpContext.RequestAborted);

                if (order == null)
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    TempData["Error"] = "Không tìm thấy đơn hàng cần thanh toán.";
                    return RedirectToAction("Orders", "Customer");
                }

                var payment = order.Payments
                    .OrderByDescending(current => current.PaymentId)
                    .FirstOrDefault();

                if (payment == null
                    || !string.Equals(
                        payment.PaymentMethod,
                        PaymentMethods.VnPay,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    TempData["Error"] = "Đơn hàng không sử dụng phương thức VNPAY.";
                    return RedirectToAction("OrderPlaced", "Checkout", new { orderId });
                }

                if (payment.PaymentStatus == PaymentStatuses.Paid
                    || payment.PaymentStatus == PaymentStatuses.Refunded)
                {
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    TempData["OrderSuccessModal"] = JsonSerializer.Serialize(
                        new { OrderId = orderId, Method = PaymentMethods.VnPay });
                    return RedirectToAction("OrderPlaced", "Checkout", new { orderId });
                }

                if (order.Status != OrderStatuses.Pending
                    || !order.IsStockDeducted
                    || payment.PaymentStatus != PaymentStatuses.AwaitingGateway)
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    TempData["Error"] = "Đơn hàng không còn ở trạng thái có thể thanh toán lại.";
                    return RedirectToAction("OrderPlaced", "Checkout", new { orderId });
                }

                var now = DateTime.Now;
                var deadline = _paymentExpirationPolicy.GetDeadline(payment);
                if (deadline.HasValue && now >= deadline.Value)
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    TempData["Error"] = "Đơn đã hết thời hạn thanh toán. Hệ thống đang giải phóng tồn kho.";
                    return RedirectToAction("OrderPlaced", "Checkout", new { orderId });
                }

                var amount = payment.Amount ?? order.TotalAmount ?? 0m;
                if (amount <= 0)
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    TempData["Error"] = "Số tiền thanh toán của đơn không hợp lệ.";
                    return RedirectToAction("OrderPlaced", "Checkout", new { orderId });
                }

                string idempotencyKey = $"VNPAY:RETRY:{payment.PaymentId}:{now:yyyyMMddHHmm}";
                bool claimed = await _paymentTransactionService.TryClaimAsync(
                    new PaymentEventClaim(
                        payment.PaymentId,
                        order.OrderId,
                        PaymentMethods.VnPay,
                        PaymentEventTypes.Retry,
                        idempotencyKey,
                        payment.ProviderTransactionId,
                        amount,
                        "RETRY",
                        "PENDING",
                        $"orderId={order.OrderId};customerId={customerId}",
                        now),
                    HttpContext.RequestAborted);

                string paymentUrl = _vnPayService.CreatePaymentUrl(
                    HttpContext,
                    order.OrderId,
                    (double)amount);

                if (string.IsNullOrWhiteSpace(paymentUrl))
                {
                    throw new InvalidOperationException("Không tạo được đường dẫn thanh toán VNPAY.");
                }

                if (claimed)
                {
                    payment.LastProcessedAt = now;
                    payment.LastResponseCode = "RETRY";
                    payment.LastTransactionStatus = "PENDING";
                    payment.FailureReason = null;

                    var extendedDeadline = _paymentExpirationPolicy.GetDeadline(
                        PaymentMethods.VnPay,
                        now);
                    if (extendedDeadline.HasValue)
                    {
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                              UPDATE OrderReservations
                              SET ExpiresAt = {extendedDeadline.Value},
                                  Reason = {"Gia hạn giữ hàng khi khách mở lại VNPAY."}
                              WHERE OrderId = {order.OrderId}
                                AND Status = {OrderReservationStatuses.Consumed}
                              """,
                            HttpContext.RequestAborted);
                    }

                    await _context.SaveChangesAsync(HttpContext.RequestAborted);
                    await _paymentTransactionService.CompleteAsync(
                        idempotencyKey,
                        PaymentEventStatuses.Processed,
                        null,
                        now,
                        HttpContext.RequestAborted);
                }

                await transaction.CommitAsync(HttpContext.RequestAborted);
                return Redirect(paymentUrl);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(HttpContext.RequestAborted);
                TempData["Error"] = "Không thể mở lại cổng VNPAY: " + ex.Message;
                return RedirectToAction("OrderPlaced", "Checkout", new { orderId });
            }
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
                System.Data.IsolationLevel.Serializable,
                HttpContext.RequestAborted);

            try
            {
                var order = await _context.Orders
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(
                        o => o.OrderId == ipnResponse.OrderId,
                        HttpContext.RequestAborted);

                if (order == null)
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    return Json(new { RspCode = "01", Message = "Order Not Found" });
                }

                var payment = order.Payments.FirstOrDefault();
                if (payment == null)
                {
                    await transaction.RollbackAsync(HttpContext.RequestAborted);
                    return Json(new { RspCode = "99", Message = "Payment Info Missing" });
                }

                string responseCode = parameters.GetValueOrDefault("vnp_ResponseCode") ?? string.Empty;
                string transactionStatus = ipnResponse.TransactionStatus ?? string.Empty;
                string providerTransactionId = string.IsNullOrWhiteSpace(ipnResponse.TransactionId)
                    ? "NO_TRANSACTION_ID"
                    : ipnResponse.TransactionId;
                string idempotencyKey = $"VNPAY:IPN:{order.OrderId}:{providerTransactionId}:{responseCode}:{transactionStatus}";
                string rawPayload = JsonSerializer.Serialize(
                    parameters.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value));
                var now = DateTime.Now;

                bool claimed = await _paymentTransactionService.TryClaimAsync(
                    new PaymentEventClaim(
                        payment.PaymentId,
                        order.OrderId,
                        PaymentMethods.VnPay,
                        PaymentEventTypes.Ipn,
                        idempotencyKey,
                        ipnResponse.TransactionId,
                        ipnResponse.Amount,
                        responseCode,
                        transactionStatus,
                        rawPayload,
                        now),
                    HttpContext.RequestAborted);

                if (!claimed)
                {
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    return Json(new { RspCode = "00", Message = "Duplicate Event" });
                }

                UpdateProviderAudit(
                    payment,
                    ipnResponse.TransactionId,
                    responseCode,
                    transactionStatus,
                    now);

                if (order.TotalAmount != ipnResponse.Amount)
                {
                    await RejectEventAsync(
                        idempotencyKey,
                        payment,
                        "Số tiền IPN không khớp tổng tiền đơn hàng.",
                        now);
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    return Json(new { RspCode = "04", Message = "Invalid Amount" });
                }

                if (!string.Equals(payment.PaymentMethod, PaymentMethods.VnPay, StringComparison.OrdinalIgnoreCase))
                {
                    await RejectEventAsync(
                        idempotencyKey,
                        payment,
                        "Phương thức thanh toán của đơn không phải VNPAY.",
                        now);
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    return Json(new { RspCode = "02", Message = "Payment Method Mismatch" });
                }

                if (transactionStatus == "00")
                {
                    if (payment.PaymentStatus == PaymentStatuses.Paid
                        || payment.PaymentStatus == PaymentStatuses.Refunded)
                    {
                        await SaveAndCompleteEventAsync(
                            idempotencyKey,
                            PaymentEventStatuses.Ignored,
                            null,
                            now);
                        await transaction.CommitAsync(HttpContext.RequestAborted);
                        return Json(new { RspCode = "00", Message = "Already Confirmed" });
                    }

                    if (order.Status == OrderStatuses.Cancelled
                        || order.Status == OrderStatuses.Returned)
                    {
                        await RejectEventAsync(
                            idempotencyKey,
                            payment,
                            "IPN thành công đến sau khi đơn đã đóng.",
                            now);
                        await transaction.CommitAsync(HttpContext.RequestAborted);
                        return Json(new { RspCode = "02", Message = "Order Closed" });
                    }

                    payment.PaymentStatus = PaymentStatuses.Paid;
                    payment.PaymentDate = now;
                    payment.Amount = ipnResponse.Amount;
                    payment.FailureReason = null;

                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                          UPDATE OrderReservations
                          SET ExpiresAt = NULL,
                              Reason = {"Thanh toán VNPAY đã được xác nhận."}
                          WHERE OrderId = {order.OrderId}
                            AND Status = {OrderReservationStatuses.Consumed}
                          """,
                        HttpContext.RequestAborted);

                    if (order.Status == OrderStatuses.Pending)
                    {
                        _orderStateService.Transition(
                            order,
                            OrderStatuses.Processing,
                            $"VNPAY IPN xác nhận thanh toán thành công. Mã GD: {ipnResponse.TransactionId}.",
                            now);
                    }

                    await SaveAndCompleteEventAsync(
                        idempotencyKey,
                        PaymentEventStatuses.Processed,
                        null,
                        now);
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    return Json(new { RspCode = "00", Message = "Confirm Success" });
                }

                if (transactionStatus == "02" || transactionStatus == "99")
                {
                    if (payment.PaymentStatus == PaymentStatuses.Failed
                        && order.Status == OrderStatuses.Cancelled)
                    {
                        await SaveAndCompleteEventAsync(
                            idempotencyKey,
                            PaymentEventStatuses.Ignored,
                            null,
                            now);
                        await transaction.CommitAsync(HttpContext.RequestAborted);
                        return Json(new { RspCode = "00", Message = "Failure Already Processed" });
                    }

                    if (payment.PaymentStatus == PaymentStatuses.Paid
                        || payment.PaymentStatus == PaymentStatuses.Refunded)
                    {
                        await RejectEventAsync(
                            idempotencyKey,
                            payment,
                            "IPN thất bại không được phép hủy một giao dịch đã thanh toán.",
                            now);
                        await transaction.CommitAsync(HttpContext.RequestAborted);
                        return Json(new { RspCode = "02", Message = "Paid Order Cannot Be Cancelled" });
                    }

                    if (order.Status != OrderStatuses.Pending
                        && order.Status != OrderStatuses.Processing)
                    {
                        await RejectEventAsync(
                            idempotencyKey,
                            payment,
                            $"Trạng thái đơn '{order.Status}' không cho phép xử lý IPN thất bại.",
                            now);
                        await transaction.CommitAsync(HttpContext.RequestAborted);
                        return Json(new { RspCode = "02", Message = "Order State Conflict" });
                    }

                    await _orderInventoryService.RestoreOrderStockAsync(
                        order.OrderId,
                        "Hoàn kho do VNPAY báo giao dịch thất bại",
                        restoreFlashSaleSlots: true,
                        occurredAt: now,
                        cancellationToken: HttpContext.RequestAborted);

                    payment.PaymentStatus = PaymentStatuses.Failed;
                    payment.FailureReason = $"VNPAY transaction status: {transactionStatus}";

                    _orderStateService.Transition(
                        order,
                        OrderStatuses.Cancelled,
                        $"VNPAY IPN báo giao dịch thất bại. Mã GD: {ipnResponse.TransactionId}.",
                        now);

                    await SaveAndCompleteEventAsync(
                        idempotencyKey,
                        PaymentEventStatuses.Processed,
                        null,
                        now);
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    return Json(new { RspCode = "00", Message = "Failure Processed" });
                }

                await SaveAndCompleteEventAsync(
                    idempotencyKey,
                    PaymentEventStatuses.Ignored,
                    null,
                    now);
                await transaction.CommitAsync(HttpContext.RequestAborted);
                return Json(new { RspCode = "00", Message = "Non-final Status Ignored" });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(HttpContext.RequestAborted);
                return Json(new { RspCode = "99", Message = "System Error" });
            }
        }

        private static void UpdateProviderAudit(
            Payments payment,
            string? providerTransactionId,
            string? responseCode,
            string? transactionStatus,
            DateTime processedAt)
        {
            if (!string.IsNullOrWhiteSpace(providerTransactionId))
            {
                payment.ProviderTransactionId = providerTransactionId;
            }

            payment.LastResponseCode = responseCode;
            payment.LastTransactionStatus = transactionStatus;
            payment.LastProcessedAt = processedAt;
        }

        private async Task RejectEventAsync(
            string idempotencyKey,
            Payments payment,
            string reason,
            DateTime processedAt)
        {
            payment.FailureReason = reason;
            await SaveAndCompleteEventAsync(
                idempotencyKey,
                PaymentEventStatuses.Rejected,
                reason,
                processedAt);
        }

        private async Task SaveAndCompleteEventAsync(
            string idempotencyKey,
            string eventStatus,
            string? errorMessage,
            DateTime processedAt)
        {
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            await _paymentTransactionService.CompleteAsync(
                idempotencyKey,
                eventStatus,
                errorMessage,
                processedAt,
                HttpContext.RequestAborted);
        }
    }
}
