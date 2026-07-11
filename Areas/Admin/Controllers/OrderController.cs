using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IOrderInventoryService _orderInventoryService;
        private readonly IOrderStateService _orderStateService;
        private readonly IPaymentTransactionService _paymentTransactionService;
        private readonly IShippingLifecycleService _shippingLifecycleService;
        private readonly IRefundSettlementService _refundSettlementService;
        private readonly IConfiguration _configuration;

        public OrderController(
            ApplicationDbContext context,
            IOrderInventoryService orderInventoryService,
            IOrderStateService orderStateService,
            IPaymentTransactionService paymentTransactionService,
            IShippingLifecycleService shippingLifecycleService,
            IRefundSettlementService refundSettlementService,
            IConfiguration configuration)
        {
            _context = context;
            _orderInventoryService = orderInventoryService;
            _orderStateService = orderStateService;
            _paymentTransactionService = paymentTransactionService;
            _shippingLifecycleService = shippingLifecycleService;
            _refundSettlementService = refundSettlementService;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index(
            string searchKeyword,
            string status,
            DateTime? fromDate,
            DateTime? toDate)
        {
            ViewBag.ReturnAlerts = await _context.OrderReturns
                .Include(r => r.Order)
                .Where(r => r.Status == ReturnStatuses.Pending && r.IsAlertAdminRead == false)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();

            ViewBag.Keyword = searchKeyword;
            ViewBag.Status = status;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            var query = _context.Orders.AsQueryable();
            bool hasSearch = !string.IsNullOrWhiteSpace(searchKeyword);
            string kw = string.Empty;
            int exactOrderId = -1;
            bool isNumeric = false;

            if (hasSearch)
            {
                kw = searchKeyword.Trim().ToLowerInvariant().Replace("#", string.Empty);
                isNumeric = int.TryParse(kw, out exactOrderId);

                query = query.Where(o => o.OrderId.ToString().Contains(kw)
                    || (o.ShippingPhone != null && o.ShippingPhone.Contains(kw)));
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(o => o.OrderDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(o => o.OrderDate < toDate.Value.Date.AddDays(1));
            }

            if (hasSearch && isNumeric)
            {
                query = query.OrderByDescending(o => o.OrderId == exactOrderId)
                    .ThenByDescending(o => o.OrderId.ToString().Contains(kw))
                    .ThenBy(o => o.OrderId);
            }
            else if (hasSearch)
            {
                query = query.OrderBy(o => o.OrderId);
            }
            else
            {
                query = query.OrderByDescending(o => o.OrderDate);
            }

            return View(await query.ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> MarkReturnAlertAsRead(int returnId)
        {
            var returnRequest = await _context.OrderReturns.FindAsync(returnId);
            if (returnRequest != null)
            {
                returnRequest.IsAlertAdminRead = true;
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.OrderHistories)
                .Include(o => o.Payments)
                .Include(o => o.Shipping).ThenInclude(s => s.ShippingEvents)
                .Include(o => o.OrderReturns)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            return order == null ? NotFound() : View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int orderId, string newStatus, string? note)
        {
            if (newStatus == OrderStatuses.Cancelled)
            {
                RefundSettlementResult cancellation =
                    await _refundSettlementService.SettleCancellationAsync(
                        new OrderCancellationCommand(
                            orderId,
                            string.IsNullOrWhiteSpace(note)
                                ? "Admin hủy đơn."
                                : note.Trim(),
                            "Admin",
                            User.Identity?.Name ?? "admin",
                            CustomerId: null,
                            AllowProcessing: true),
                        HttpContext.RequestAborted);

                return Json(new
                {
                    success = cancellation.Success,
                    message = cancellation.Message,
                    requiresReview = cancellation.RequiresReview,
                    transactionReference = cancellation.TransactionReference
                });
            }

            using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                    .Include(o => o.Shipping)
                    .Include(o => o.Customer)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Không tìm thấy đơn hàng." });
                }

                if (string.Equals(order.Status, newStatus, StringComparison.Ordinal))
                {
                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "Trạng thái đã được cập nhật trước đó." });
                }

                if (!OrderStatuses.CanTransition(order.Status, newStatus))
                {
                    throw new InvalidOperationException(
                        $"Không thể chuyển trạng thái từ '{order.Status}' sang '{newStatus}'.");
                }

                var payment = order.Payments.FirstOrDefault();
                string transitionNote = note ?? string.Empty;
                DateTime now = DateTime.Now;

                if (newStatus == OrderStatuses.Processing)
                {
                    bool requiresPrepayment = payment != null
                        && !string.Equals(
                            payment.PaymentMethod,
                            PaymentMethods.Cod,
                            StringComparison.OrdinalIgnoreCase);

                    if (requiresPrepayment
                        && payment!.PaymentStatus != PaymentStatuses.Paid)
                    {
                        throw new InvalidOperationException(
                            "Đơn thanh toán online/chuyển khoản phải được xác nhận đã thanh toán trước khi xử lý.");
                    }

                    bool deductedNow = await _orderInventoryService.DeductOrderStockAsync(
                        order.OrderId,
                        "Trừ kho khi admin xác nhận đơn cũ",
                        occurredAt: now,
                        cancellationToken: HttpContext.RequestAborted);

                    ShippingCreationResult shipment =
                        await _shippingLifecycleService.EnsureShipmentCreatedAsync(
                            order.OrderId,
                            HttpContext.RequestAborted);

                    transitionNote = string.IsNullOrWhiteSpace(transitionNote)
                        ? (deductedNow
                            ? "Admin xác nhận đơn. Tồn kho của đơn cũ vừa được trừ. "
                            : "Admin xác nhận đơn. Tồn kho đã được giữ/trừ trước đó. ")
                            + shipment.Message
                        : transitionNote + " " + shipment.Message;
                }
                else if (newStatus == OrderStatuses.Shipping)
                {
                    await _shippingLifecycleService.SyncManualOrderStatusAsync(
                        order.OrderId,
                        OrderStatuses.Shipping,
                        now,
                        HttpContext.RequestAborted);

                    transitionNote = string.IsNullOrWhiteSpace(transitionNote)
                        ? "Đã xuất kho và bàn giao kiện hàng cho đơn vị vận chuyển."
                        : transitionNote;
                }
                else if (newStatus == OrderStatuses.Delivered)
                {
                    await _shippingLifecycleService.SyncManualOrderStatusAsync(
                        order.OrderId,
                        OrderStatuses.Delivered,
                        now,
                        HttpContext.RequestAborted);

                    transitionNote = string.IsNullOrWhiteSpace(transitionNote)
                        ? "Đơn vị vận chuyển xác nhận phát hàng thành công."
                        : transitionNote;

                    if (payment != null
                        && string.Equals(payment.PaymentMethod, PaymentMethods.Cod, StringComparison.OrdinalIgnoreCase)
                        && payment.PaymentStatus != PaymentStatuses.Paid)
                    {
                        payment.PaymentStatus = PaymentStatuses.Paid;
                        payment.PaymentDate = now;
                    }
                }
                else if (newStatus == OrderStatuses.Completed)
                {
                    int pointsEarned = (int)((order.TotalAmount ?? 0) / 100000);
                    if (pointsEarned > 0 && order.Customer != null)
                    {
                        order.Customer.RewardPoints += pointsEarned;
                        int points = order.Customer.RewardPoints;
                        order.Customer.CustomerType = points >= 600
                            ? "Kim Cương"
                            : points >= 300
                                ? "Vàng"
                                : points >= 100
                                    ? "Bạc"
                                    : "Newbie";

                        transitionNote = (transitionNote ?? string.Empty)
                            + $" [Hệ thống: +{pointsEarned} điểm].";
                    }

                    if (payment != null
                        && string.Equals(payment.PaymentMethod, PaymentMethods.Cod, StringComparison.OrdinalIgnoreCase)
                        && payment.PaymentStatus != PaymentStatuses.Paid)
                    {
                        payment.PaymentStatus = PaymentStatuses.Paid;
                        payment.PaymentDate = now;
                    }
                }


                _orderStateService.Transition(
                    order,
                    newStatus,
                    transitionNote,
                    now);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = ex.Message });
            }
        }

        public async Task<IActionResult> ExportToExcel(
            string searchKeyword,
            string status,
            DateTime? fromDate,
            DateTime? toDate)
        {
            var query = _context.Orders.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchKeyword))
            {
                string kw = searchKeyword.Trim().ToLowerInvariant().Replace("#", string.Empty);
                query = query.Where(o => o.OrderId.ToString().Contains(kw)
                    || (o.ShippingPhone != null && o.ShippingPhone.Contains(kw)));
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(o => o.OrderDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(o => o.OrderDate < toDate.Value.Date.AddDays(1));
            }

            var orders = await query.OrderByDescending(o => o.OrderDate).ToListAsync();
            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("Mã Đơn Hàng,Khách Hàng,Số Điện Thoại,Ngày Khởi Tạo,Tổng Giá Trị,Trạng Thái");

            foreach (var order in orders)
            {
                csvBuilder.AppendLine(
                    $"#ORD-{order.OrderId},{order.ShippingFullName},{order.ShippingPhone},"
                    + $"{order.OrderDate?.ToString("dd/MM/yyyy HH:mm")},{order.TotalAmount},{order.Status}");
            }

            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var csvBytes = Encoding.UTF8.GetBytes(csvBuilder.ToString());

            return File(
                bom.Concat(csvBytes).ToArray(),
                "text/csv",
                $"BaoCao_DonHang_{DateTime.Now:yyyyMMdd}.csv");
        }

        public async Task<IActionResult> PrintInvoice(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails).ThenInclude(d => d.Variant).ThenInclude(v => v.Product)
                .Include(o => o.Shipping)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            return order == null ? NotFound() : View(order);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> BankPaymentWebhook([FromBody] BankTransferModel gatewayData)
        {
            string configuredSecret = _configuration["BankTransferWebhook:Secret"] ?? string.Empty;
            string providedSecret = Request.Headers["X-Webhook-Secret"].ToString();

            // Môi trường sandbox có thể để trống Secret để webhook test gọi trực tiếp.
            // Khi có cấu hình Secret, request vẫn phải gửi X-Webhook-Secret khớp chính xác.
            if (!string.IsNullOrWhiteSpace(configuredSecret))
            {
                bool validSecret = CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(configuredSecret),
                    Encoding.UTF8.GetBytes(providedSecret));

                if (!validSecret)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Webhook signature không hợp lệ."
                    });
                }
            }

            if (gatewayData == null || string.IsNullOrWhiteSpace(gatewayData.TransactionId))
            {
                return Json(new { success = false, message = "Thiếu mã giao dịch." });
            }

            using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            try
            {
                var order = await _context.Orders
                    .Include(o => o.OrderDetails).ThenInclude(d => d.Variant)
                    .Include(o => o.Payments)
                    .Include(o => o.Shipping)
                    .FirstOrDefaultAsync(o => o.OrderId == gatewayData.OrderIdReference);

                if (order == null || OrderStatuses.IsTerminal(order.Status))
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Đơn hàng không hợp lệ." });
                }

                var payment = order.Payments.FirstOrDefault();
                if (payment == null
                    || !string.Equals(payment.PaymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Phương thức thanh toán không khớp." });
                }

                string idempotencyKey = $"BANK:WEBHOOK:{gatewayData.TransactionId}";
                var now = DateTime.Now;
                bool claimed = await _paymentTransactionService.TryClaimAsync(
                    new PaymentEventClaim(
                        payment.PaymentId,
                        order.OrderId,
                        PaymentMethods.BankTransfer,
                        PaymentEventTypes.BankWebhook,
                        idempotencyKey,
                        gatewayData.TransactionId,
                        gatewayData.AmountTransferred,
                        "RECEIVED",
                        "00",
                        JsonSerializer.Serialize(gatewayData),
                        now),
                    HttpContext.RequestAborted);

                if (!claimed)
                {
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    return Json(new { success = true, message = "Giao dịch đã được xử lý trước đó." });
                }

                payment.ProviderTransactionId = gatewayData.TransactionId;
                payment.LastResponseCode = "RECEIVED";
                payment.LastTransactionStatus = "00";
                payment.LastProcessedAt = now;

                if (payment.PaymentStatus == PaymentStatuses.Paid)
                {
                    await _context.SaveChangesAsync(HttpContext.RequestAborted);
                    await _paymentTransactionService.CompleteAsync(
                        idempotencyKey,
                        PaymentEventStatuses.Ignored,
                        processedAt: now,
                        cancellationToken: HttpContext.RequestAborted);
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    return Json(new { success = true, message = "Đơn đã được thanh toán trước đó." });
                }

                if (gatewayData.AmountTransferred < (order.TotalAmount ?? 0))
                {
                    string error = "Số tiền chuyển khoản chưa đủ.";
                    payment.FailureReason = error;
                    await _context.SaveChangesAsync(HttpContext.RequestAborted);
                    await _paymentTransactionService.CompleteAsync(
                        idempotencyKey,
                        PaymentEventStatuses.Rejected,
                        error,
                        now,
                        HttpContext.RequestAborted);
                    await transaction.CommitAsync(HttpContext.RequestAborted);
                    return Json(new { success = false, message = error });
                }

                if (order.Status == OrderStatuses.Pending)
                {
                    await _orderInventoryService.DeductOrderStockAsync(
                        order.OrderId,
                        "Trừ kho khi webhook chuyển khoản xác nhận đơn cũ",
                        occurredAt: now,
                        cancellationToken: HttpContext.RequestAborted);

                    _orderStateService.Transition(
                        order,
                        OrderStatuses.Processing,
                        $"Ngân hàng xác nhận chuyển khoản. Mã GD: {gatewayData.TransactionId}.",
                        now);

                    await _shippingLifecycleService.EnsureShipmentCreatedAsync(
                        order.OrderId,
                        HttpContext.RequestAborted);
                }
                else if (order.Status != OrderStatuses.Processing)
                {
                    throw new InvalidOperationException(
                        $"Không thể xác nhận thanh toán khi đơn đang ở trạng thái '{order.Status}'.");
                }

                payment.PaymentStatus = PaymentStatuses.Paid;
                payment.PaymentDate = now;
                payment.Amount = gatewayData.AmountTransferred;
                payment.FailureReason = null;

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                      UPDATE OrderReservations
                      SET ExpiresAt = NULL,
                          Reason = {"Chuyển khoản ngân hàng đã được xác nhận."}
                      WHERE OrderId = {order.OrderId}
                        AND Status = {OrderReservationStatuses.Consumed}
                      """,
                    HttpContext.RequestAborted);

                await _context.SaveChangesAsync(HttpContext.RequestAborted);
                await _paymentTransactionService.CompleteAsync(
                    idempotencyKey,
                    PaymentEventStatuses.Processed,
                    processedAt: now,
                    cancellationToken: HttpContext.RequestAborted);
                await transaction.CommitAsync(HttpContext.RequestAborted);
                return Json(new { success = true });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Không thể xử lý webhook thanh toán." });
            }
        }
    }

    public class BankTransferModel
    {
        public int OrderIdReference { get; set; }
        public decimal AmountTransferred { get; set; }
        public string TransactionId { get; set; } = string.Empty;
    }
}
