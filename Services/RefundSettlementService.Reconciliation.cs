using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed partial class RefundSettlementService
{
    private const string ReturnRefundPrefix = "REFUND:RETURN:";
    private const string CancellationRefundPrefix = "REFUND:CANCEL:";

    public async Task<RefundSettlementResult> ReconcileRefundAsync(
        RefundReconciliationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.PaymentTransactionId <= 0)
        {
            return new RefundSettlementResult(
                false,
                false,
                false,
                "Mã sự kiện hoàn tiền không hợp lệ.");
        }

        ReconciliationSnapshot? snapshot =
            await LoadReconciliationSnapshotAsync(
                command.PaymentTransactionId,
                cancellationToken);

        if (snapshot == null)
        {
            return new RefundSettlementResult(
                false,
                false,
                false,
                "Không tìm thấy sự kiện hoàn tiền.");
        }

        if (snapshot.Status == PaymentEventStatuses.Processed)
        {
            return new RefundSettlementResult(
                true,
                true,
                false,
                "Sự kiện hoàn tiền đã được chốt trước đó.",
                snapshot.OrderId,
                snapshot.Amount,
                snapshot.ProviderTransactionId);
        }

        if (!command.ProviderConfirmedSuccess)
        {
            return await MarkReconciledFailureAsync(
                snapshot,
                command,
                cancellationToken);
        }

        string transactionReference =
            command.TransactionReference?.Trim()
            ?? snapshot.ProviderTransactionId?.Trim()
            ?? string.Empty;

        bool isManualRefund =
            string.Equals(
                snapshot.ResponseCode,
                "MANUAL_REQUIRED",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                snapshot.Provider,
                PaymentMethods.VnPay,
                StringComparison.OrdinalIgnoreCase);

        if (isManualRefund
            && string.IsNullOrWhiteSpace(transactionReference))
        {
            return new RefundSettlementResult(
                false,
                false,
                true,
                "Vui lòng nhập mã giao dịch hoàn tiền thủ công.",
                snapshot.OrderId,
                snapshot.Amount);
        }

        if (string.IsNullOrWhiteSpace(transactionReference))
        {
            transactionReference =
                $"RECONCILED-{snapshot.PaymentTransactionId}";
        }

        var providerResult = new RefundProviderResult(
            true,
            string.IsNullOrWhiteSpace(snapshot.Provider)
                ? "MANUAL"
                : snapshot.Provider,
            transactionReference,
            "RECONCILED_SUCCESS",
            "CONFIRMED",
            string.IsNullOrWhiteSpace(command.AdminNote)
                ? "Admin xác nhận provider đã hoàn tiền sau đối soát."
                : command.AdminNote.Trim(),
            $"event={snapshot.PaymentTransactionId};"
                + $"reference={transactionReference};"
                + $"actor={command.Actor};"
                + $"note={command.AdminNote?.Trim()}");

        try
        {
            if (TryParseKey(
                    snapshot.IdempotencyKey,
                    ReturnRefundPrefix,
                    out int returnId))
            {
                if (!command.IsProductIntact.HasValue)
                {
                    return new RefundSettlementResult(
                        false,
                        false,
                        true,
                        "Cần chọn tình trạng hàng hoàn để quyết toán tồn kho.",
                        snapshot.OrderId,
                        snapshot.Amount,
                        transactionReference);
                }

                return await FinalizeSuccessAsync(
                    new ReturnRefundCommand(
                        returnId,
                        command.IsProductIntact.Value,
                        command.AdminNote,
                        transactionReference,
                        command.Actor),
                    snapshot.IdempotencyKey,
                    providerResult,
                    cancellationToken);
            }

            if (TryParseKey(
                    snapshot.IdempotencyKey,
                    CancellationRefundPrefix,
                    out int orderId))
            {
                CancellationContext? cancellationContext =
                    await LoadCancellationContextAsync(
                        orderId,
                        cancellationToken);

                if (cancellationContext == null)
                {
                    return new RefundSettlementResult(
                        false,
                        false,
                        true,
                        "Không tìm thấy đơn hàng để chốt đối soát hủy đơn.",
                        snapshot.OrderId,
                        snapshot.Amount,
                        transactionReference);
                }

                if (cancellationContext.OrderStatus
                    == OrderStatuses.Cancelled)
                {
                    return await FinalizeExistingCancellationRefundAsync(
                        snapshot,
                        providerResult,
                        cancellationToken);
                }

                var prepared = new PreparedCancellation(
                    cancellationContext.OrderId,
                    cancellationContext.PaymentId,
                    snapshot.Amount,
                    cancellationContext.PaymentMethod,
                    cancellationContext.TransactionDate,
                    false,
                    false,
                    false,
                    null);

                string reason =
                    cancellationContext.CancellationReason;
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = string.IsNullOrWhiteSpace(command.AdminNote)
                        ? "Admin chốt hủy đơn sau đối soát hoàn tiền."
                        : command.AdminNote.Trim();
                }

                return await FinalizeCancellationAsync(
                    new OrderCancellationCommand(
                        cancellationContext.OrderId,
                        reason,
                        string.IsNullOrWhiteSpace(
                            cancellationContext.RequestedBy)
                            ? "Admin-Reconciliation"
                            : cancellationContext.RequestedBy,
                        command.Actor,
                        CustomerId: null,
                        AllowProcessing: true),
                    snapshot.IdempotencyKey,
                    prepared,
                    providerResult,
                    cancellationToken);
            }

            return new RefundSettlementResult(
                false,
                false,
                true,
                "Không nhận diện được loại nghiệp vụ từ idempotency key.",
                snapshot.OrderId,
                snapshot.Amount,
                transactionReference);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Không thể chốt refund reconciliation event {EventId}.",
                snapshot.PaymentTransactionId);

            await MarkRequiresReviewAsync(
                snapshot.IdempotencyKey,
                providerResult,
                ex.Message,
                cancellationToken);

            return new RefundSettlementResult(
                false,
                false,
                true,
                "Provider đã được xác nhận hoàn tiền nhưng hệ thống chưa chốt được nghiệp vụ: "
                    + ex.Message,
                snapshot.OrderId,
                snapshot.Amount,
                transactionReference);
        }
    }

    private async Task<RefundSettlementResult>
        MarkReconciledFailureAsync(
            ReconciliationSnapshot snapshot,
            RefundReconciliationCommand command,
            CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            PaymentTransactions refundEvent =
                await _context.PaymentTransactions
                    .FirstOrDefaultAsync(
                        current => current.PaymentTransactionId
                            == snapshot.PaymentTransactionId,
                        cancellationToken)
                ?? throw new InvalidOperationException(
                    "Sự kiện refund không còn tồn tại.");

            if (refundEvent.Status
                == PaymentEventStatuses.Processed)
            {
                await transaction.CommitAsync(cancellationToken);
                return new RefundSettlementResult(
                    true,
                    true,
                    false,
                    "Refund đã được chốt thành công trước đó.",
                    refundEvent.OrderId,
                    refundEvent.Amount ?? 0,
                    refundEvent.ProviderTransactionId);
            }

            DateTime now = DateTime.Now;
            refundEvent.Status = PaymentEventStatuses.Failed;
            refundEvent.ResponseCode = "RECONCILED_FAILED";
            refundEvent.TransactionStatus = "NOT_REFUNDED";
            refundEvent.ProcessedAt = now;
            refundEvent.ErrorMessage =
                string.IsNullOrWhiteSpace(command.AdminNote)
                    ? "Admin xác nhận provider chưa hoàn tiền sau đối soát."
                    : command.AdminNote.Trim();

            Payments? payment = await _context.Payments
                .FirstOrDefaultAsync(
                    current => current.PaymentId
                        == refundEvent.PaymentId,
                    cancellationToken);

            if (payment != null)
            {
                payment.PaymentStatus =
                    PaymentStatuses.AwaitingRefund;
                payment.LastResponseCode =
                    refundEvent.ResponseCode;
                payment.LastTransactionStatus =
                    refundEvent.TransactionStatus;
                payment.LastProcessedAt = now;
                payment.FailureReason =
                    refundEvent.ErrorMessage;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new RefundSettlementResult(
                true,
                false,
                false,
                "Đã xác nhận provider chưa hoàn tiền. Event được mở lại ở trạng thái Failed.",
                refundEvent.OrderId,
                refundEvent.Amount ?? 0,
                refundEvent.ProviderTransactionId);
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

    private async Task<ReconciliationSnapshot?>
        LoadReconciliationSnapshotAsync(
            long paymentTransactionId,
            CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        return await _context.PaymentTransactions
            .AsNoTracking()
            .Where(current =>
                current.PaymentTransactionId
                    == paymentTransactionId
                && current.EventType
                    == PaymentEventTypes.Refund)
            .Select(current => new ReconciliationSnapshot(
                current.PaymentTransactionId,
                current.PaymentId,
                current.OrderId,
                current.Provider,
                current.IdempotencyKey,
                current.Amount ?? 0,
                current.Status,
                current.ResponseCode,
                current.TransactionStatus,
                current.ProviderTransactionId,
                current.ErrorMessage))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CancellationContext?>
        LoadCancellationContextAsync(
            int orderId,
            CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        Orders? order = await _context.Orders
            .AsNoTracking()
            .Include(current => current.Payments)
            .FirstOrDefaultAsync(
                current => current.OrderId == orderId,
                cancellationToken);

        if (order == null)
        {
            return null;
        }

        Payments? payment = order.Payments
            .OrderByDescending(current => current.PaymentId)
            .FirstOrDefault();

        return new CancellationContext(
            order.OrderId,
            payment?.PaymentId,
            payment?.PaymentMethod ?? string.Empty,
            payment?.PaymentDate?.ToString("yyyyMMddHHmmss")
                ?? DateTime.Now.ToString("yyyyMMddHHmmss"),
            order.Status ?? string.Empty,
            order.CancellationReason ?? string.Empty,
            order.CancellationRequestedBy ?? string.Empty);
    }

    private async Task<RefundSettlementResult>
        FinalizeExistingCancellationRefundAsync(
            ReconciliationSnapshot snapshot,
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
            PaymentTransactions refundEvent =
                await _context.PaymentTransactions
                    .FirstOrDefaultAsync(
                        current => current.PaymentTransactionId
                            == snapshot.PaymentTransactionId,
                        cancellationToken)
                ?? throw new InvalidOperationException(
                    "Không tìm thấy refund event khi chốt đối soát.");

            if (refundEvent.Status
                == PaymentEventStatuses.Processed)
            {
                await transaction.CommitAsync(cancellationToken);
                return new RefundSettlementResult(
                    true,
                    true,
                    false,
                    "Khoản hoàn tiền đã được chốt trước đó.",
                    refundEvent.OrderId,
                    refundEvent.Amount ?? 0,
                    refundEvent.ProviderTransactionId);
            }

            Orders order = await _context.Orders
                .FirstOrDefaultAsync(
                    current => current.OrderId == refundEvent.OrderId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Không tìm thấy đơn đã hủy để chốt payment.");

            Payments payment = await _context.Payments
                .FirstOrDefaultAsync(
                    current => current.PaymentId == refundEvent.PaymentId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Không tìm thấy payment để chốt đối soát.");

            if (order.Status != OrderStatuses.Cancelled)
            {
                throw new InvalidOperationException(
                    "Đơn không còn ở trạng thái đã hủy.");
            }

            DateTime now = DateTime.Now;
            payment.PaymentStatus = PaymentStatuses.Refunded;
            payment.LastResponseCode = providerResult.ResponseCode;
            payment.LastTransactionStatus =
                providerResult.TransactionStatus;
            payment.LastProcessedAt = now;
            payment.FailureReason = null;

            refundEvent.Status = PaymentEventStatuses.Processed;
            ApplyProviderResult(
                refundEvent,
                providerResult,
                now);

            bool historyExists = await _context.OrderHistories
                .AnyAsync(
                    current => current.OrderId == order.OrderId
                        && current.Status == OrderStatuses.Cancelled
                        && current.Note != null
                        && current.Note.Contains(
                            $"Refund event #{refundEvent.PaymentTransactionId}"),
                    cancellationToken);

            if (!historyExists)
            {
                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = OrderStatuses.Cancelled,
                    UpdatedAt = now,
                    Note = "Đã đối soát hoàn tiền cho đơn đã hủy. "
                        + $"Refund event #{refundEvent.PaymentTransactionId}. "
                        + $"Mã GD: {providerResult.TransactionReference}."
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new RefundSettlementResult(
                true,
                false,
                false,
                "Đã chốt hoàn tiền cho đơn đã hủy; không xử lý lại kho hoặc vận đơn.",
                order.OrderId,
                refundEvent.Amount ?? 0,
                providerResult.TransactionReference);
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

    private static bool TryParseKey(
        string? key,
        string prefix,
        out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(key)
            || !key.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
            key[prefix.Length..],
            out id)
            && id > 0;
    }

    private sealed record ReconciliationSnapshot(
        long PaymentTransactionId,
        int PaymentId,
        int OrderId,
        string Provider,
        string IdempotencyKey,
        decimal Amount,
        string Status,
        string? ResponseCode,
        string? TransactionStatus,
        string? ProviderTransactionId,
        string? ErrorMessage);

    private sealed record CancellationContext(
        int OrderId,
        int? PaymentId,
        string PaymentMethod,
        string TransactionDate,
        string OrderStatus,
        string CancellationReason,
        string RequestedBy);
}
