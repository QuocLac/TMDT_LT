using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class UnpaidOrderExpirationService : IUnpaidOrderExpirationService
{
    private readonly ApplicationDbContext _context;
    private readonly PaymentExpirationPolicy _expirationPolicy;
    private readonly IPaymentTransactionService _paymentTransactionService;
    private readonly IOrderInventoryService _orderInventoryService;
    private readonly IOrderStateService _orderStateService;
    private readonly ILogger<UnpaidOrderExpirationService> _logger;

    public UnpaidOrderExpirationService(
        ApplicationDbContext context,
        PaymentExpirationPolicy expirationPolicy,
        IPaymentTransactionService paymentTransactionService,
        IOrderInventoryService orderInventoryService,
        IOrderStateService orderStateService,
        ILogger<UnpaidOrderExpirationService> logger)
    {
        _context = context;
        _expirationPolicy = expirationPolicy;
        _paymentTransactionService = paymentTransactionService;
        _orderInventoryService = orderInventoryService;
        _orderStateService = orderStateService;
        _logger = logger;
    }

    public async Task<int> ExpireDueOrdersAsync(CancellationToken cancellationToken = default)
    {
        var options = _expirationPolicy.Current;
        if (!options.Enabled)
        {
            return 0;
        }

        var now = DateTime.Now;
        var vnPayCutoff = now.AddMinutes(-Math.Max(1, options.VnPayTimeoutMinutes));
        var bankTransferCutoff = now.AddMinutes(-Math.Max(1, options.BankTransferTimeoutMinutes));

        var candidatePaymentIds = await _context.Payments
            .AsNoTracking()
            .Where(payment =>
                payment.OrderId.HasValue
                && payment.Order != null
                && payment.Order.Status == OrderStatuses.Pending
                && (
                    (payment.PaymentMethod == PaymentMethods.VnPay
                        && payment.PaymentStatus == PaymentStatuses.AwaitingGateway
                        && payment.CreatedAt <= vnPayCutoff)
                    ||
                    (payment.PaymentMethod == PaymentMethods.BankTransfer
                        && payment.PaymentStatus == PaymentStatuses.AwaitingBankTransfer
                        && payment.CreatedAt <= bankTransferCutoff)
                ))
            .OrderBy(payment => payment.CreatedAt)
            .Select(payment => payment.PaymentId)
            .Take(Math.Clamp(options.BatchSize, 1, 500))
            .ToListAsync(cancellationToken);

        var expiredCount = 0;

        foreach (var paymentId in candidatePaymentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _context.ChangeTracker.Clear();

            try
            {
                if (await TryExpirePaymentAsync(paymentId, now, cancellationToken))
                {
                    expiredCount++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể hết hạn payment {PaymentId}; worker sẽ thử lại ở vòng sau.",
                    paymentId);
            }
        }

        _context.ChangeTracker.Clear();
        return expiredCount;
    }

    private async Task<bool> TryExpirePaymentAsync(
        int paymentId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var payment = await _context.Payments
            .Include(current => current.Order)
            .FirstOrDefaultAsync(current => current.PaymentId == paymentId, cancellationToken);

        if (payment?.Order == null
            || payment.Order.Status != OrderStatuses.Pending
            || !_expirationPolicy.IsExpired(payment, now))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var expectedStatus = _expirationPolicy.GetAwaitingStatus(payment.PaymentMethod);
        var deadline = _expirationPolicy.GetDeadline(payment);
        if (expectedStatus == null || !deadline.HasValue)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var reason = string.Equals(
                payment.PaymentMethod,
                PaymentMethods.VnPay,
                StringComparison.OrdinalIgnoreCase)
            ? "Đơn VNPAY quá thời hạn thanh toán."
            : "Đơn chuyển khoản quá thời hạn chờ đối soát.";

        var idempotencyKey = $"SYSTEM:PAYMENT_EXPIRED:{payment.PaymentId}:{deadline.Value:yyyyMMddHHmmss}";
        var claimed = await _paymentTransactionService.TryClaimAsync(
            new PaymentEventClaim(
                payment.PaymentId,
                payment.Order.OrderId,
                "SYSTEM",
                PaymentEventTypes.Expired,
                idempotencyKey,
                payment.ProviderTransactionId,
                payment.Amount ?? payment.Order.TotalAmount,
                "EXPIRED",
                "EXPIRED",
                $"paymentId={payment.PaymentId};deadline={deadline.Value:O}",
                now),
            cancellationToken);

        if (!claimed)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
              UPDATE Payments
              SET PaymentStatus = {PaymentStatuses.Failed},
                  FailureReason = {reason},
                  LastResponseCode = {"EXPIRED"},
                  LastTransactionStatus = {"EXPIRED"},
                  LastProcessedAt = {now}
              WHERE PaymentId = {payment.PaymentId}
                AND PaymentStatus = {expectedStatus}
              """,
            cancellationToken);

        if (affected == 0)
        {
            await _paymentTransactionService.CompleteAsync(
                idempotencyKey,
                PaymentEventStatuses.Ignored,
                "Payment đã được xử lý bởi luồng khác trước khi worker claim trạng thái.",
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        payment.PaymentStatus = PaymentStatuses.Failed;
        payment.FailureReason = reason;
        payment.LastResponseCode = "EXPIRED";
        payment.LastTransactionStatus = "EXPIRED";
        payment.LastProcessedAt = now;

        var stockRestored = await _orderInventoryService.RestoreOrderStockAsync(
            payment.Order.OrderId,
            reason,
            restoreFlashSaleSlots: true,
            occurredAt: now,
            cancellationToken: cancellationToken);

        payment.Order.CancellationReason = reason;
        payment.Order.CancellationRequestedBy = "System";

        _orderStateService.Transition(
            payment.Order,
            OrderStatuses.Cancelled,
            stockRestored
                ? $"{reason} Hệ thống đã hoàn kho và hoàn suất Flash Sale."
                : $"{reason} Tồn kho của đơn đã được giải phóng trước đó.",
            now);

        await _context.SaveChangesAsync(cancellationToken);
        await _paymentTransactionService.CompleteAsync(
            idempotencyKey,
            PaymentEventStatuses.Processed,
            null,
            now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
