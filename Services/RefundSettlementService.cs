using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class RefundSettlementService : IRefundSettlementService
{
    private readonly ApplicationDbContext _context;
    private readonly VnPayService _vnPayService;
    private readonly IOrderInventoryService _orderInventoryService;
    private readonly IOrderStateService _orderStateService;
    private readonly ILogger<RefundSettlementService> _logger;

    public RefundSettlementService(
        ApplicationDbContext context,
        VnPayService vnPayService,
        IOrderInventoryService orderInventoryService,
        IOrderStateService orderStateService,
        ILogger<RefundSettlementService> logger)
    {
        _context = context;
        _vnPayService = vnPayService;
        _orderInventoryService = orderInventoryService;
        _orderStateService = orderStateService;
        _logger = logger;
    }

    public async Task<RefundSettlementResult> SettleReturnAsync(
        ReturnRefundCommand command,
        CancellationToken cancellationToken = default)
    {
        string eventKey = $"REFUND:RETURN:{command.ReturnId}";
        PreparedRefund? prepared = await PrepareAsync(
            command,
            eventKey,
            cancellationToken);

        if (prepared == null)
        {
            return new RefundSettlementResult(
                false,
                false,
                false,
                "Không thể chuẩn bị yêu cầu hoàn tiền.");
        }

        if (prepared.CompletedResult != null)
        {
            return prepared.CompletedResult;
        }

        RefundProviderResult providerResult;
        if (prepared.IsVnPay)
        {
            string requestId =
                $"RET{command.ReturnId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            VnPayRefundResult result =
                await _vnPayService.RequestRefundAsync(
                    prepared.OrderId,
                    prepared.Amount,
                    prepared.TransactionDate,
                    command.Actor,
                    requestId,
                    cancellationToken);

            providerResult = new RefundProviderResult(
                result.Success,
                PaymentMethods.VnPay,
                result.ProviderTransactionId ?? result.RequestId,
                result.ResponseCode,
                result.TransactionStatus,
                result.Message,
                result.RawResponse);
        }
        else
        {
            string manualReference =
                command.ManualTransactionReference?.Trim()
                ?? string.Empty;

            providerResult = new RefundProviderResult(
                true,
                prepared.PaymentMethod,
                manualReference,
                "MANUAL_CONFIRMED",
                "CONFIRMED",
                "Admin đã xác nhận giao dịch hoàn tiền thủ công.",
                manualReference);
        }

        if (!providerResult.Success)
        {
            await MarkProviderFailureAsync(
                eventKey,
                providerResult,
                cancellationToken);

            return new RefundSettlementResult(
                false,
                false,
                false,
                providerResult.Message,
                prepared.OrderId,
                prepared.Amount,
                providerResult.TransactionReference,
                prepared.CustomerEmail,
                prepared.CustomerName);
        }

        try
        {
            return await FinalizeSuccessAsync(
                command,
                eventKey,
                providerResult,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Provider đã xác nhận hoàn tiền nhưng DB chưa chốt được hồ sơ trả hàng {ReturnId}.",
                command.ReturnId);

            await MarkRequiresReviewAsync(
                eventKey,
                providerResult,
                ex.Message,
                cancellationToken);

            return new RefundSettlementResult(
                false,
                false,
                true,
                "Provider đã phản hồi thành công nhưng hệ thống chưa chốt được dữ liệu. Không bấm hoàn tiền lại; cần đối soát sự kiện refund.",
                prepared.OrderId,
                prepared.Amount,
                providerResult.TransactionReference,
                prepared.CustomerEmail,
                prepared.CustomerName);
        }
    }

    private async Task<PreparedRefund?> PrepareAsync(
        ReturnRefundCommand command,
        string eventKey,
        CancellationToken cancellationToken)
    {
        if (command.ReturnId <= 0)
        {
            return new PreparedRefund(
                0,
                0,
                0,
                string.Empty,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                new RefundSettlementResult(
                    false,
                    false,
                    false,
                    "Mã hồ sơ hoàn trả không hợp lệ."));
        }

        _context.ChangeTracker.Clear();

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var returnRequest = await _context.OrderReturns
                .Include(current => current.Order)
                    .ThenInclude(order => order!.Payments)
                .Include(current => current.Customer)
                    .ThenInclude(customer => customer!.Account)
                .FirstOrDefaultAsync(
                    current => current.ReturnId == command.ReturnId,
                    cancellationToken);

            if (returnRequest?.Order == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new PreparedRefund(
                    0,
                    0,
                    0,
                    string.Empty,
                    false,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    new RefundSettlementResult(
                        false,
                        false,
                        false,
                        "Không tìm thấy hồ sơ hoàn trả."));
            }

            var order = returnRequest.Order;
            var payment = order.Payments
                .OrderByDescending(current => current.PaymentId)
                .FirstOrDefault();

            if (payment == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new PreparedRefund(
                    order.OrderId,
                    0,
                    order.TotalAmount ?? 0,
                    string.Empty,
                    false,
                    string.Empty,
                    returnRequest.Customer?.Account?.Email ?? string.Empty,
                    returnRequest.Customer?.FullName ?? "Quý khách",
                    new RefundSettlementResult(
                        false,
                        false,
                        false,
                        "Không tìm thấy thông tin thanh toán của đơn hàng.",
                        order.OrderId));
            }

            if (returnRequest.Status == ReturnStatuses.Refunded
                && order.Status == OrderStatuses.Returned
                && payment.PaymentStatus == PaymentStatuses.Refunded)
            {
                await transaction.CommitAsync(cancellationToken);
                return new PreparedRefund(
                    order.OrderId,
                    payment.PaymentId,
                    order.TotalAmount ?? 0,
                    payment.PaymentMethod ?? string.Empty,
                    string.Equals(
                        payment.PaymentMethod,
                        PaymentMethods.VnPay,
                        StringComparison.OrdinalIgnoreCase),
                    payment.PaymentDate?.ToString("yyyyMMddHHmmss")
                        ?? DateTime.Now.ToString("yyyyMMddHHmmss"),
                    returnRequest.Customer?.Account?.Email ?? string.Empty,
                    returnRequest.Customer?.FullName ?? "Quý khách",
                    new RefundSettlementResult(
                        true,
                        true,
                        false,
                        "Hồ sơ đã được hoàn tiền trước đó.",
                        order.OrderId,
                        order.TotalAmount ?? 0));
            }

            if (returnRequest.Status != ReturnStatuses.Inspecting
                || order.Status != OrderStatuses.ReturnInspecting)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new PreparedRefund(
                    order.OrderId,
                    payment.PaymentId,
                    order.TotalAmount ?? 0,
                    payment.PaymentMethod ?? string.Empty,
                    false,
                    string.Empty,
                    returnRequest.Customer?.Account?.Email ?? string.Empty,
                    returnRequest.Customer?.FullName ?? "Quý khách",
                    new RefundSettlementResult(
                        false,
                        false,
                        false,
                        "Hồ sơ chưa đến bước kiểm định hoặc trạng thái đơn không đồng bộ.",
                        order.OrderId));
            }

            bool isVnPay = string.Equals(
                payment.PaymentMethod,
                PaymentMethods.VnPay,
                StringComparison.OrdinalIgnoreCase);

            if (isVnPay
                && payment.PaymentStatus != PaymentStatuses.Paid
                && payment.PaymentStatus != PaymentStatuses.AwaitingRefund)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new PreparedRefund(
                    order.OrderId,
                    payment.PaymentId,
                    order.TotalAmount ?? 0,
                    payment.PaymentMethod ?? string.Empty,
                    true,
                    string.Empty,
                    returnRequest.Customer?.Account?.Email ?? string.Empty,
                    returnRequest.Customer?.FullName ?? "Quý khách",
                    new RefundSettlementResult(
                        false,
                        false,
                        false,
                        "Dòng tiền VNPAY không ở trạng thái có thể hoàn.",
                        order.OrderId));
            }

            if (!isVnPay
                && string.IsNullOrWhiteSpace(
                    command.ManualTransactionReference))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new PreparedRefund(
                    order.OrderId,
                    payment.PaymentId,
                    order.TotalAmount ?? 0,
                    payment.PaymentMethod ?? string.Empty,
                    false,
                    string.Empty,
                    returnRequest.Customer?.Account?.Email ?? string.Empty,
                    returnRequest.Customer?.FullName ?? "Quý khách",
                    new RefundSettlementResult(
                        false,
                        false,
                        false,
                        "Vui lòng nhập mã giao dịch hoàn tiền ngân hàng.",
                        order.OrderId));
            }

            PaymentTransactions? refundEvent =
                await _context.PaymentTransactions
                    .FirstOrDefaultAsync(
                        current =>
                            current.IdempotencyKey == eventKey,
                        cancellationToken);

            if (refundEvent != null)
            {
                if (refundEvent.Status == PaymentEventStatuses.Processed)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new PreparedRefund(
                        order.OrderId,
                        payment.PaymentId,
                        order.TotalAmount ?? 0,
                        payment.PaymentMethod ?? string.Empty,
                        isVnPay,
                        string.Empty,
                        returnRequest.Customer?.Account?.Email ?? string.Empty,
                        returnRequest.Customer?.FullName ?? "Quý khách",
                        new RefundSettlementResult(
                            false,
                            false,
                            true,
                            "Refund event đã hoàn tất nhưng trạng thái hồ sơ chưa đồng bộ. Cần đối soát, không gọi provider lần nữa.",
                            order.OrderId));
                }

                if (refundEvent.Status == PaymentEventStatuses.Processing
                    || refundEvent.Status == PaymentEventStatuses.Received
                    || refundEvent.Status
                        == PaymentEventStatuses.RequiresReview)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new PreparedRefund(
                        order.OrderId,
                        payment.PaymentId,
                        order.TotalAmount ?? 0,
                        payment.PaymentMethod ?? string.Empty,
                        isVnPay,
                        string.Empty,
                        returnRequest.Customer?.Account?.Email ?? string.Empty,
                        returnRequest.Customer?.FullName ?? "Quý khách",
                        new RefundSettlementResult(
                            false,
                            false,
                            true,
                            "Yêu cầu hoàn tiền đang xử lý hoặc cần đối soát. Không gửi lại lệnh provider.",
                            order.OrderId,
                            order.TotalAmount ?? 0,
                            refundEvent.ProviderTransactionId));
                }

                refundEvent.Provider =
                    payment.PaymentMethod ?? "MANUAL";
                refundEvent.Status =
                    PaymentEventStatuses.Processing;
                refundEvent.ProviderTransactionId = null;
                refundEvent.ResponseCode = null;
                refundEvent.TransactionStatus = null;
                refundEvent.PayloadHash = null;
                refundEvent.ErrorMessage = null;
                refundEvent.ReceivedAt = DateTime.Now;
                refundEvent.ProcessedAt = null;
                refundEvent.Amount = order.TotalAmount;
            }
            else
            {
                _context.PaymentTransactions.Add(
                    new PaymentTransactions
                    {
                        PaymentId = payment.PaymentId,
                        OrderId = order.OrderId,
                        Provider =
                            payment.PaymentMethod ?? "MANUAL",
                        EventType = PaymentEventTypes.Refund,
                        IdempotencyKey = eventKey,
                        Amount = order.TotalAmount,
                        Status =
                            PaymentEventStatuses.Processing,
                        ReceivedAt = DateTime.Now
                    });
            }

            payment.PaymentStatus =
                PaymentStatuses.AwaitingRefund;
            payment.LastProcessedAt = DateTime.Now;
            payment.FailureReason = null;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            string transactionDate =
                payment.PaymentDate?.ToString("yyyyMMddHHmmss")
                ?? DateTime.Now.ToString("yyyyMMddHHmmss");

            return new PreparedRefund(
                order.OrderId,
                payment.PaymentId,
                order.TotalAmount ?? 0,
                payment.PaymentMethod ?? string.Empty,
                isVnPay,
                transactionDate,
                returnRequest.Customer?.Account?.Email
                    ?? string.Empty,
                returnRequest.Customer?.FullName
                    ?? "Quý khách",
                null);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    private async Task MarkProviderFailureAsync(
        string eventKey,
        RefundProviderResult result,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            PaymentTransactions? refundEvent =
                await _context.PaymentTransactions
                    .FirstOrDefaultAsync(
                        current =>
                            current.IdempotencyKey == eventKey,
                        cancellationToken);

            if (refundEvent != null)
            {
                refundEvent.Status =
                    PaymentEventStatuses.Failed;
                ApplyProviderResult(refundEvent, result);
            }

            Payments? payment = refundEvent == null
                ? null
                : await _context.Payments
                    .FirstOrDefaultAsync(
                        current =>
                            current.PaymentId
                            == refundEvent.PaymentId,
                        cancellationToken);

            if (payment != null)
            {
                payment.PaymentStatus =
                    PaymentStatuses.AwaitingRefund;
                payment.LastResponseCode =
                    result.ResponseCode;
                payment.LastTransactionStatus =
                    result.TransactionStatus;
                payment.LastProcessedAt = DateTime.Now;
                payment.FailureReason = result.Message;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    private async Task<RefundSettlementResult>
        FinalizeSuccessAsync(
            ReturnRefundCommand command,
            string eventKey,
            RefundProviderResult providerResult,
            CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var returnRequest = await _context.OrderReturns
                .Include(current => current.Order)
                    .ThenInclude(order => order!.OrderDetails)
                        .ThenInclude(detail => detail.Variant)
                .Include(current => current.Order)
                    .ThenInclude(order => order!.Payments)
                .Include(current => current.Customer)
                    .ThenInclude(customer => customer!.Account)
                .FirstOrDefaultAsync(
                    current => current.ReturnId == command.ReturnId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Không tìm thấy hồ sơ hoàn trả khi chốt refund.");

            var order = returnRequest.Order
                ?? throw new InvalidOperationException(
                    "Hồ sơ hoàn trả không còn liên kết đơn hàng.");
            var payment = order.Payments
                .OrderByDescending(current => current.PaymentId)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Không tìm thấy payment khi chốt refund.");

            PaymentTransactions refundEvent =
                await _context.PaymentTransactions
                    .FirstOrDefaultAsync(
                        current =>
                            current.IdempotencyKey == eventKey,
                        cancellationToken)
                ?? throw new InvalidOperationException(
                    "Không tìm thấy refund event để chốt.");

            if (returnRequest.Status == ReturnStatuses.Refunded
                && order.Status == OrderStatuses.Returned
                && payment.PaymentStatus == PaymentStatuses.Refunded)
            {
                refundEvent.Status =
                    PaymentEventStatuses.Processed;
                ApplyProviderResult(
                    refundEvent,
                    providerResult);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new RefundSettlementResult(
                    true,
                    true,
                    false,
                    "Hồ sơ đã được chốt trước đó.",
                    order.OrderId,
                    order.TotalAmount ?? 0,
                    providerResult.TransactionReference,
                    returnRequest.Customer?.Account?.Email,
                    returnRequest.Customer?.FullName);
            }

            if (returnRequest.Status != ReturnStatuses.Inspecting
                || order.Status != OrderStatuses.ReturnInspecting)
            {
                throw new InvalidOperationException(
                    "Trạng thái hồ sơ thay đổi trong lúc provider xử lý.");
            }

            if (!order.IsStockDeducted)
            {
                throw new InvalidOperationException(
                    "Tồn kho của đơn đã được quyết toán trước khi refund được chốt.");
            }

            DateTime now = DateTime.Now;
            bool stockClosed;
            string settlementNote;

            if (command.IsProductIntact)
            {
                stockClosed =
                    await _orderInventoryService
                        .RestoreOrderStockAsync(
                            order.OrderId,
                            "Nhập lại kho từ đơn hoàn trả còn nguyên",
                            restoreFlashSaleSlots: false,
                            occurredAt: now,
                            cancellationToken:
                                cancellationToken);
                settlementNote =
                    "[Nhập lại kho] "
                    + (command.AdminNote?.Trim()
                        ?? string.Empty);
            }
            else
            {
                stockClosed =
                    await _orderInventoryService
                        .CloseOrderStockWithoutRestockAsync(
                            order.OrderId,
                            "Hàng hoàn bị hỏng, không nhập lại tồn bán",
                            occurredAt: now,
                            cancellationToken:
                                cancellationToken);
                settlementNote =
                    "[Hàng hỏng/không nhập lại kho] "
                    + (command.AdminNote?.Trim()
                        ?? string.Empty);
            }

            if (!stockClosed)
            {
                throw new InvalidOperationException(
                    "Trạng thái tồn kho đã được đóng trước đó.");
            }

            returnRequest.Status =
                ReturnStatuses.Refunded;
            returnRequest.ResolvedAt = now;
            returnRequest.AdminNote =
                settlementNote.Trim();

            payment.PaymentStatus =
                PaymentStatuses.Refunded;
            payment.LastResponseCode =
                providerResult.ResponseCode;
            payment.LastTransactionStatus =
                providerResult.TransactionStatus;
            payment.LastProcessedAt = now;
            payment.FailureReason = null;

            refundEvent.Status =
                PaymentEventStatuses.Processed;
            ApplyProviderResult(
                refundEvent,
                providerResult,
                now);

            _orderStateService.Transition(
                order,
                OrderStatuses.Returned,
                $"Hoàn tiền thành công. {settlementNote}. "
                + $"Mã GD: {providerResult.TransactionReference}.",
                now);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new RefundSettlementResult(
                true,
                false,
                false,
                "Đã chốt hoàn tiền, tồn kho và trạng thái đơn hàng.",
                order.OrderId,
                order.TotalAmount ?? 0,
                providerResult.TransactionReference,
                returnRequest.Customer?.Account?.Email,
                returnRequest.Customer?.FullName
                    ?? "Quý khách");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    private async Task MarkRequiresReviewAsync(
        string eventKey,
        RefundProviderResult providerResult,
        string error,
        CancellationToken cancellationToken)
    {
        try
        {
            _context.ChangeTracker.Clear();

            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    cancellationToken);

            PaymentTransactions? refundEvent =
                await _context.PaymentTransactions
                    .FirstOrDefaultAsync(
                        current =>
                            current.IdempotencyKey == eventKey,
                        cancellationToken);

            if (refundEvent != null)
            {
                refundEvent.Status =
                    PaymentEventStatuses.RequiresReview;
                ApplyProviderResult(
                    refundEvent,
                    providerResult);
                refundEvent.ErrorMessage =
                    "Provider success; DB settlement failed: "
                    + error;

                Payments? payment =
                    await _context.Payments
                        .FirstOrDefaultAsync(
                            current =>
                                current.PaymentId
                                == refundEvent.PaymentId,
                            cancellationToken);

                if (payment != null)
                {
                    payment.PaymentStatus =
                        PaymentStatuses.AwaitingRefund;
                    payment.LastResponseCode =
                        providerResult.ResponseCode;
                    payment.LastTransactionStatus =
                        providerResult.TransactionStatus;
                    payment.LastProcessedAt = DateTime.Now;
                    payment.FailureReason =
                        "Provider đã xác nhận refund nhưng DB cần đối soát.";
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception markException)
        {
            _logger.LogCritical(
                markException,
                "Không thể đánh dấu refund event {EventKey} cần đối soát.",
                eventKey);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    private static void ApplyProviderResult(
        PaymentTransactions refundEvent,
        RefundProviderResult result,
        DateTime? processedAt = null)
    {
        refundEvent.Provider =
            result.Provider;
        refundEvent.ProviderTransactionId =
            result.TransactionReference;
        refundEvent.ResponseCode =
            result.ResponseCode;
        refundEvent.TransactionStatus =
            result.TransactionStatus;
        refundEvent.PayloadHash =
            ComputeSha256(result.RawPayload);
        refundEvent.ErrorMessage =
            result.Success ? null : result.Message;
        refundEvent.ProcessedAt =
            processedAt ?? DateTime.Now;
    }

    private static string? ComputeSha256(
        string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        byte[] bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)
            .ToLowerInvariant();
    }

    private sealed record PreparedRefund(
        int OrderId,
        int PaymentId,
        decimal Amount,
        string PaymentMethod,
        bool IsVnPay,
        string TransactionDate,
        string CustomerEmail,
        string CustomerName,
        RefundSettlementResult? CompletedResult);

    private sealed record RefundProviderResult(
        bool Success,
        string Provider,
        string? TransactionReference,
        string ResponseCode,
        string TransactionStatus,
        string Message,
        string RawPayload);
}
