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
        private readonly IOrderStateService _orderStateService;
        private readonly IRefundSettlementService _refundSettlementService;

        public ReturnController(
            ApplicationDbContext context,
            IConfiguration config,
            IOrderStateService orderStateService,
            IRefundSettlementService refundSettlementService)
        {
            _context = context;
            _config = config;
            _orderStateService = orderStateService;
            _refundSettlementService = refundSettlementService;
        }

        private async Task SendEmailAsync(
            string toEmail,
            string subject,
            string body)
        {
            try
            {
                IConfigurationSection smtpSettings =
                    _config.GetSection("SmtpSettings");
                string? senderEmail =
                    smtpSettings["SenderEmail"];
                string? senderName =
                    smtpSettings["SenderName"];
                string? server =
                    smtpSettings["Server"];
                string? password =
                    smtpSettings["Password"];

                if (string.IsNullOrWhiteSpace(senderEmail)
                    || string.IsNullOrWhiteSpace(server)
                    || !int.TryParse(
                        smtpSettings["Port"],
                        out int port))
                {
                    return;
                }

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(
                        senderEmail,
                        senderName ?? "PHONE.ST"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                using var smtpClient = new SmtpClient(server)
                {
                    Port = port,
                    Credentials = new NetworkCredential(
                        senderEmail,
                        password ?? string.Empty),
                    EnableSsl = true
                };

                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Lỗi gửi Email: " + ex.Message);
            }
        }

        public async Task<IActionResult> Index(
            string status)
        {
            var query = _context.OrderReturns
                .Include(current => current.Order)
                .Include(current => current.Customer)
                    .ThenInclude(customer => customer.Account)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(
                    current => current.Status == status);
            }

            ViewBag.CurrentStatus = status;
            return View(
                await query
                    .OrderByDescending(
                        current => current.CreatedAt)
                    .ToListAsync());
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

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    HttpContext.RequestAborted);

            try
            {
                var returnRequest =
                    await _context.OrderReturns
                        .Include(current => current.Order)
                            .ThenInclude(order => order!.Payments)
                        .Include(current => current.Customer)
                            .ThenInclude(customer => customer!.Account)
                        .FirstOrDefaultAsync(
                            current =>
                                current.ReturnId == returnId,
                            HttpContext.RequestAborted);

                if (returnRequest?.Order == null)
                {
                    await transaction.RollbackAsync(
                        HttpContext.RequestAborted);
                    return Json(new
                    {
                        success = false,
                        message =
                            "Không tìm thấy hồ sơ khiếu nại."
                    });
                }

                if (returnRequest.Status == newStatus)
                {
                    await transaction.CommitAsync(
                        HttpContext.RequestAborted);
                    return Json(new
                    {
                        success = true,
                        message =
                            "Trạng thái đã được cập nhật trước đó."
                    });
                }

                if (!ReturnStatuses.CanTransition(
                        returnRequest.Status,
                        newStatus))
                {
                    throw new InvalidOperationException(
                        $"Không thể chuyển hồ sơ từ "
                        + $"'{returnRequest.Status}' "
                        + $"sang '{newStatus}'.");
                }

                if (newStatus == ReturnStatuses.Rejected)
                {
                    string refundEventKey =
                        $"REFUND:RETURN:{returnId}";
                    PaymentTransactions? activeRefundEvent =
                        await _context.PaymentTransactions
                            .FirstOrDefaultAsync(
                                current =>
                                    current.IdempotencyKey
                                    == refundEventKey,
                                HttpContext.RequestAborted);

                    if (activeRefundEvent != null
                        && activeRefundEvent.Status
                            != PaymentEventStatuses.Failed
                        && activeRefundEvent.Status
                            != PaymentEventStatuses.Rejected)
                    {
                        throw new InvalidOperationException(
                            "Không thể từ chối hồ sơ vì lệnh hoàn tiền "
                            + "đã được chuẩn bị, đang xử lý hoặc cần đối soát.");
                    }
                }

                Orders order = returnRequest.Order;
                string orderTargetStatus;
                string historyNote;
                DateTime now = DateTime.Now;

                if (newStatus == ReturnStatuses.Rejected)
                {
                    orderTargetStatus =
                        OrderStatuses.Completed;
                    returnRequest.ResolvedAt = now;
                    historyNote =
                        $"Yêu cầu trả hàng bị từ chối. "
                        + $"Lý do Admin: {adminNote}.";
                    sendRejectedEmail = true;

                    Payments? payment = order.Payments
                        .OrderByDescending(
                            current => current.PaymentId)
                        .FirstOrDefault();

                    if (payment != null
                        && (payment.PaymentStatus
                                == PaymentStatuses.AwaitingRefund
                            || string.Equals(
                                payment.PaymentMethod,
                                PaymentMethods.Cod,
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        payment.PaymentStatus =
                            PaymentStatuses.Paid;
                        payment.PaymentDate ??= now;
                    }
                }
                else if (newStatus
                    == ReturnStatuses.AwaitingCustomer)
                {
                    orderTargetStatus =
                        OrderStatuses.ReturnAwaitingCustomer;
                    historyNote =
                        $"Yêu cầu trả hàng được chấp nhận. "
                        + $"Ghi chú: {adminNote}.";
                    sendAcceptedEmail = true;
                }
                else if (newStatus
                    == ReturnStatuses.Inspecting)
                {
                    orderTargetStatus =
                        OrderStatuses.ReturnInspecting;
                    historyNote =
                        $"Kho đã nhận hàng và bắt đầu kiểm định. "
                        + $"Ghi chú: {adminNote}.";
                }
                else
                {
                    throw new InvalidOperationException(
                        "Trạng thái trả hàng không được hỗ trợ ở bước này.");
                }

                returnRequest.Status = newStatus;
                returnRequest.AdminNote =
                    adminNote?.Trim();

                _orderStateService.Transition(
                    order,
                    orderTargetStatus,
                    historyNote,
                    now);

                await _context.SaveChangesAsync(
                    HttpContext.RequestAborted);
                await transaction.CommitAsync(
                    HttpContext.RequestAborted);

                customerEmail =
                    returnRequest.Customer?.Account?.Email
                    ?? string.Empty;
                customerName =
                    returnRequest.Customer?.FullName
                    ?? "Quý khách";
                orderId = returnRequest.OrderId;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(
                    HttpContext.RequestAborted);
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                if (sendAcceptedEmail)
                {
                    string body =
                        $"<h3>Chào {customerName},</h3>"
                        + $"<p>Yêu cầu trả hàng cho đơn "
                        + $"<b>#ORD-{orderId}</b> "
                        + $"đã được chấp nhận.</p>"
                        + $"<p><b>Ghi chú:</b> "
                        + $"{adminNote}</p>";

                    await SendEmailAsync(
                        customerEmail,
                        $"[PHONE.ST] Yêu cầu trả hàng "
                        + $"#ORD-{orderId} được chấp nhận",
                        body);
                }
                else if (sendRejectedEmail)
                {
                    string body =
                        $"<h3>Chào {customerName},</h3>"
                        + $"<p>Yêu cầu đổi trả cho đơn "
                        + $"<b>#ORD-{orderId}</b> "
                        + $"đã bị từ chối.</p>"
                        + $"<p><b>Lý do:</b> "
                        + $"{adminNote}</p>";

                    await SendEmailAsync(
                        customerEmail,
                        $"[PHONE.ST] Cập nhật khiếu nại "
                        + $"#ORD-{orderId}",
                        body);
                }
            }

            return Json(new
            {
                success = true,
                message = "Đã cập nhật tiến trình."
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessRefund(
            int returnId,
            bool isProductIntact,
            string adminNote,
            string transactionRef)
        {
            RefundSettlementResult result =
                await _refundSettlementService
                    .SettleReturnAsync(
                        new ReturnRefundCommand(
                            returnId,
                            isProductIntact,
                            adminNote,
                            transactionRef,
                            User.Identity?.Name ?? "admin"),
                        HttpContext.RequestAborted);

            if (!result.Success)
            {
                return Json(new
                {
                    success = false,
                    requiresReview =
                        result.RequiresReview,
                    message = result.Message
                });
            }

            if (!result.AlreadyProcessed
                && !string.IsNullOrWhiteSpace(
                    result.CustomerEmail)
                && result.OrderId.HasValue)
            {
                string body =
                    $"<h3>Chào "
                    + $"{result.CustomerName ?? "Quý khách"},"
                    + $"</h3>"
                    + $"<p>Cửa hàng đã hoàn tiền cho đơn "
                    + $"<b>#ORD-{result.OrderId.Value}</b>."
                    + $"</p>"
                    + $"<p>Số tiền: "
                    + $"<b>{result.RefundedAmount:N0}đ</b>."
                    + $"</p>"
                    + $"<p>Mã giao dịch: "
                    + $"<b>{result.TransactionReference}</b>."
                    + $"</p>";

                await SendEmailAsync(
                    result.CustomerEmail!,
                    $"[PHONE.ST] Biên lai hoàn tiền "
                    + $"#ORD-{result.OrderId.Value}",
                    body);
            }

            return Json(new
            {
                success = true,
                alreadyProcessed =
                    result.AlreadyProcessed,
                message = result.Message,
                transactionRef =
                    result.TransactionReference
            });
        }
    }
}
