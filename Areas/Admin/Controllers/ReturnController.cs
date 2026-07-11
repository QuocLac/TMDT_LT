using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ReturnController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly VnPayService _vnPayService;
        private readonly IOrderInventoryService _orderInventoryService;
        private readonly IOrderStateService _orderStateService;

        public ReturnController(
            ApplicationDbContext context,
            IConfiguration config,
            VnPayService vnPayService,
            IOrderInventoryService orderInventoryService,
            IOrderStateService orderStateService)
        {
            _context = context;
            _config = config;
            _vnPayService = vnPayService;
            _orderInventoryService = orderInventoryService;
            _orderStateService = orderStateService;
        }

        private async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var smtpSettings = _config.GetSection("SmtpSettings");
                var senderEmail = smtpSettings["SenderEmail"];
                var senderName = smtpSettings["SenderName"];
                var server = smtpSettings["Server"];
                var password = smtpSettings["Password"];

                if (string.IsNullOrWhiteSpace(senderEmail)
                    || string.IsNullOrWhiteSpace(server)
                    || !int.TryParse(smtpSettings["Port"], out int port))
                {
                    return;
                }

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName ?? "PHONE.ST"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                using var smtpClient = new SmtpClient(server)
                {
                    Port = port,
                    Credentials = new NetworkCredential(senderEmail, password ?? string.Empty),
                    EnableSsl = true
                };

                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi gửi Email: " + ex.Message);
            }
        }

        public async Task<IActionResult> Index(string status)
        {
            var query = _context.OrderReturns
                .Include(r => r.Order)
                .Include(r => r.Customer)
                    .ThenInclude(c => c.Account)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(r => r.Status == status);
            }

            ViewBag.CurrentStatus = status;
            return View(await query.OrderByDescending(r => r.CreatedAt).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReturnStatus(
            int returnId,
            string newStatus,
            string adminNote)
        {
            string customerEmail = string.Empty;
            string customerName = string.Empty;
            int orderId = 0;
            bool sendAcceptedEmail = false;
            bool sendRejectedEmail = false;

            using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            try
            {
                var returnRequest = await _context.OrderReturns
                    .Include(r => r.Order).ThenInclude(o => o.Payments)
                    .Include(r => r.Customer).ThenInclude(c => c.Account)
                    .FirstOrDefaultAsync(r => r.ReturnId == returnId);

                if (returnRequest == null || returnRequest.Order == null)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Không tìm thấy hồ sơ khiếu nại." });
                }

                if (returnRequest.Status == newStatus)
                {
                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "Trạng thái đã được cập nhật trước đó." });
                }

                if (!ReturnStatuses.CanTransition(returnRequest.Status, newStatus))
                {
                    throw new InvalidOperationException(
                        $"Không thể chuyển hồ sơ từ '{returnRequest.Status}' sang '{newStatus}'.");
                }

                var order = returnRequest.Order;
                string orderTargetStatus;
                string historyNote;

                if (newStatus == ReturnStatuses.Rejected)
                {
                    orderTargetStatus = OrderStatuses.Completed;
                    returnRequest.ResolvedAt = DateTime.Now;
                    historyNote = $"Yêu cầu trả hàng bị từ chối. Lý do Admin: {adminNote}.";
                    sendRejectedEmail = true;

                    var payment = order.Payments.FirstOrDefault();
                    if (payment != null
                        && (payment.PaymentStatus == PaymentStatuses.AwaitingRefund
                            || string.Equals(payment.PaymentMethod, PaymentMethods.Cod, StringComparison.OrdinalIgnoreCase)))
                    {
                        payment.PaymentStatus = PaymentStatuses.Paid;
                        payment.PaymentDate ??= DateTime.Now;
                    }
                }
                else if (newStatus == ReturnStatuses.AwaitingCustomer)
                {
                    orderTargetStatus = OrderStatuses.ReturnAwaitingCustomer;
                    historyNote = $"Yêu cầu trả hàng được chấp nhận. Ghi chú: {adminNote}.";
                    sendAcceptedEmail = true;
                }
                else if (newStatus == ReturnStatuses.Inspecting)
                {
                    orderTargetStatus = OrderStatuses.ReturnInspecting;
                    historyNote = $"Kho đã nhận hàng và bắt đầu kiểm định. Ghi chú: {adminNote}.";
                }
                else
                {
                    throw new InvalidOperationException("Trạng thái trả hàng không được hỗ trợ ở bước này.");
                }

                returnRequest.Status = newStatus;
                returnRequest.AdminNote = adminNote?.Trim();

                _orderStateService.Transition(
                    order,
                    orderTargetStatus,
                    historyNote,
                    DateTime.Now);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                customerEmail = returnRequest.Customer?.Account?.Email ?? string.Empty;
                customerName = returnRequest.Customer?.FullName ?? "Quý khách";
                orderId = returnRequest.OrderId;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = ex.Message });
            }

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                if (sendAcceptedEmail)
                {
                    string body = $"<h3>Chào {customerName},</h3>"
                        + $"<p>Yêu cầu trả hàng cho đơn <b>#ORD-{orderId}</b> đã được chấp nhận.</p>"
                        + $"<p><b>Ghi chú:</b> {adminNote}</p>";
                    await SendEmailAsync(
                        customerEmail,
                        $"[PHONE.ST] Yêu cầu trả hàng #ORD-{orderId} được chấp nhận",
                        body);
                }
                else if (sendRejectedEmail)
                {
                    string body = $"<h3>Chào {customerName},</h3>"
                        + $"<p>Yêu cầu đổi trả cho đơn <b>#ORD-{orderId}</b> đã bị từ chối.</p>"
                        + $"<p><b>Lý do:</b> {adminNote}</p>";
                    await SendEmailAsync(
                        customerEmail,
                        $"[PHONE.ST] Cập nhật khiếu nại #ORD-{orderId}",
                        body);
                }
            }

            return Json(new { success = true, message = "Đã cập nhật tiến trình." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessRefund(
            int returnId,
            bool isProductIntact,
            string adminNote,
            string transactionRef)
        {
            string customerEmail = string.Empty;
            string customerName = string.Empty;
            int completedOrderId = 0;
            decimal refundedAmount = 0;
            string finalTransactionRef = transactionRef?.Trim() ?? string.Empty;

            using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            try
            {
                var returnRequest = await _context.OrderReturns
                    .Include(r => r.Order).ThenInclude(o => o.OrderDetails).ThenInclude(od => od.Variant)
                    .Include(r => r.Order).ThenInclude(o => o.Payments)
                    .Include(r => r.Customer).ThenInclude(c => c.Account)
                    .FirstOrDefaultAsync(r => r.ReturnId == returnId);

                if (returnRequest == null || returnRequest.Order == null)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Không tìm thấy hồ sơ hoàn trả." });
                }

                if (returnRequest.Status == ReturnStatuses.Refunded
                    && returnRequest.Order.Status == OrderStatuses.Returned)
                {
                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "Hồ sơ đã được hoàn tiền trước đó." });
                }

                if (returnRequest.Status != ReturnStatuses.Inspecting
                    || returnRequest.Order.Status != OrderStatuses.ReturnInspecting)
                {
                    await transaction.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "Hồ sơ chưa đến bước kiểm định hoặc trạng thái đơn không đồng bộ."
                    });
                }

                var order = returnRequest.Order;
                var payment = order.Payments.FirstOrDefault();

                if (!order.IsStockDeducted)
                {
                    throw new InvalidOperationException(
                        "Tồn kho của đơn đã được quyết toán trước đó; không thể hoàn tiền lặp.");
                }

                if (payment == null)
                {
                    throw new InvalidOperationException("Không tìm thấy thông tin thanh toán của đơn hàng.");
                }

                if (string.Equals(payment.PaymentMethod, PaymentMethods.VnPay, StringComparison.OrdinalIgnoreCase))
                {
                    if (payment.PaymentStatus != PaymentStatuses.Paid
                        && payment.PaymentStatus != PaymentStatuses.AwaitingRefund)
                    {
                        throw new InvalidOperationException("Dòng tiền VNPAY không ở trạng thái có thể hoàn.");
                    }

                    string transactionDate = payment.PaymentDate?.ToString("yyyyMMddHHmmss")
                        ?? DateTime.Now.ToString("yyyyMMddHHmmss");

                    bool refunded = await _vnPayService.RequestBankRefundAsync(
                        order.OrderId,
                        order.TotalAmount ?? 0,
                        transactionDate,
                        User.Identity?.Name ?? "admin");

                    if (!refunded)
                    {
                        throw new InvalidOperationException("Cổng VNPAY từ chối lệnh hoàn tiền.");
                    }

                    finalTransactionRef = $"VNPAY_REFUND_{order.OrderId}_{DateTime.Now:yyyyMMddHHmmss}";
                }
                else if (string.IsNullOrWhiteSpace(finalTransactionRef))
                {
                    await transaction.RollbackAsync();
                    return Json(new
                    {
                        success = false,
                        message = "Vui lòng nhập mã giao dịch hoàn tiền ngân hàng."
                    });
                }

                bool stockClosed;
                if (isProductIntact)
                {
                    stockClosed = await _orderInventoryService.RestoreOrderStockAsync(
                        order.OrderId,
                        "Nhập lại kho từ đơn hoàn trả còn nguyên",
                        restoreFlashSaleSlots: false,
                        occurredAt: DateTime.Now,
                        cancellationToken: HttpContext.RequestAborted);
                    adminNote = "[Nhập lại kho] " + adminNote;
                }
                else
                {
                    stockClosed = await _orderInventoryService.CloseOrderStockWithoutRestockAsync(
                        order.OrderId,
                        "Hàng hoàn bị hỏng, không nhập lại tồn bán",
                        occurredAt: DateTime.Now,
                        cancellationToken: HttpContext.RequestAborted);
                    adminNote = "[Hàng hỏng/không nhập lại kho] " + adminNote;
                }

                if (!stockClosed)
                {
                    throw new InvalidOperationException(
                        "Trạng thái tồn kho của đơn đã được đóng trước đó; không thể quyết toán lặp.");
                }

                returnRequest.Status = ReturnStatuses.Refunded;
                returnRequest.ResolvedAt = DateTime.Now;
                returnRequest.AdminNote = adminNote?.Trim();
                payment.PaymentStatus = PaymentStatuses.Refunded;

                _orderStateService.Transition(
                    order,
                    OrderStatuses.Returned,
                    $"Hoàn tiền thành công. {adminNote}. Mã GD: {finalTransactionRef}.",
                    DateTime.Now);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                customerEmail = returnRequest.Customer?.Account?.Email ?? string.Empty;
                customerName = returnRequest.Customer?.FullName ?? "Quý khách";
                completedOrderId = order.OrderId;
                refundedAmount = order.TotalAmount ?? 0;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Lỗi xử lý hệ thống: " + ex.Message });
            }

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                string body = $"<h3>Chào {customerName},</h3>"
                    + $"<p>Cửa hàng đã hoàn tiền cho đơn <b>#ORD-{completedOrderId}</b>.</p>"
                    + $"<p>Số tiền: <b>{refundedAmount:N0}đ</b>.</p>"
                    + $"<p>Mã giao dịch: <b>{finalTransactionRef}</b>.</p>";

                await SendEmailAsync(
                    customerEmail,
                    $"[PHONE.ST] Biên lai hoàn tiền #ORD-{completedOrderId}",
                    body);
            }

            return Json(new
            {
                success = true,
                message = "Đã chốt hoàn tiền, tồn kho và trạng thái đơn hàng."
            });
        }
    }
}
