using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed partial class RefundSettlementService
{
    public async Task<RefundSettlementResult> SettleCancellationAsync(
        OrderCancellationCommand command,
        CancellationToken cancellationToken = default)
    {
        string eventKey = $"REFUND:CANCEL:{command.OrderId}";
        PreparedCancellation prepared;

        try
        {
            prepared = await PrepareCancellationAsync(
                command,
                eventKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Không thể chuẩn bị hủy đơn {OrderId}.",
                command.OrderId);

            return new RefundSettlementResult(
                false,
                false,
                false,
                "Không thể chuẩn bị yêu cầu hủy đơn: " + ex.Message,
                command.OrderId);
        }

        if (prepared.CompletedResult != null)
        {
            return prepared.CompletedResult;
        }

        RefundProviderResult? providerResult = null;
        bool providerConfirmed = false;

        if (prepared.RequiresVnPayRefund)
        {
            string requestId =
                $"CAN{command.OrderId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";

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
                    providerResult.TransactionReference);
            }

            providerConfirmed = true;
        }

        try
        {
            return await FinalizeCancellationAsync(
                command,
                eventKey,
                prepared,
                providerResult,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Không thể chốt hủy đơn {OrderId} sau bước refund.",
                command.OrderId);

            if (providerConfirmed && providerResult != null)
            {
                await MarkRequiresReviewAsync(
                    eventKey,
                    providerResult,
                    ex.Message,
                    cancellationToken);

                return new RefundSettlementResult(
                    false,
                    false,
                    true,
                    "VNPAY đã xác nhận hoàn tiền nhưng hệ thống chưa chốt được hủy đơn. Không gửi lại lệnh refund; cần đối soát.",
                    prepared.OrderId,
                    prepared.Amount,
                    providerResult.TransactionReference);
            }

            return new RefundSettlementResult(
                false,
                false,
                false,
                "Không thể hủy đơn: " + ex.Message,
                prepared.OrderId,
                prepared.Amount);
        }
    }

    private async Task<PreparedCancellation> PrepareCancellationAsync(
        OrderCancellationCommand command,
        string eventKey,
        CancellationToken cancellationToken)
    {
        if (command.OrderId <= 0)
        {
            return PreparedCancellation.WithResult(
                command.OrderId,
                new RefundSettlementResult(
                    false,
                    false,
                    false,
                    "Mã đơn hàng không hợp lệ."));
        }

        string reason = command.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return PreparedCancellation.WithResult(
                command.OrderId,
                new RefundSettlementResult(
                    false,
                    false,
                    false,
                    "Vui lòng nhập lý do hủy đơn.",
                    command.OrderId));
        }

        _context.ChangeTracker.Clear();

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var order = await _context.Orders
                .Include(current => current.Payments)
                .FirstOrDefaultAsync(
                    current => current.OrderId == command.OrderId
                        && (!command.CustomerId.HasValue
                            || current.CustomerId == command.CustomerId.Value),
                    cancellationToken);

            if (order == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return PreparedCancellation.WithResult(
                    command.OrderId,
                    new RefundSettlementResult(
                        false,
                        false,
                        false,
                        "Không tìm thấy đơn hàng hoặc bạn không có quyền hủy đơn này.",
                        command.OrderId));
            }

            var payment = order.Payments
                .OrderByDescending(current => current.PaymentId)
                .FirstOrDefault();

            if (order.Status == OrderStatuses.Cancelled)
            {
                bool awaitingManualRefund = payment?.PaymentStatus
                    == PaymentStatuses.AwaitingRefund;

                await transaction.CommitAsync(cancellationToken);
                return PreparedCancellation.WithResult(
                    order.OrderId,
                    new RefundSettlementResult(
                        true,
                        true,
                        awaitingManualRefund,
                        awaitingManualRefund
                            ? "Đơn đã được hủy trước đó và đang chờ đối soát hoàn tiền thủ công."
                            : "Đơn hàng đã được hủy trước đó.",
                        order.OrderId,
                        order.TotalAmount ?? 0,
                        payment?.ProviderTransactionId));
            }

            bool canCancel = OrderStatuses.CanTransition(
                order.Status,
                OrderStatuses.Cancelled);

            if (!canCancel
                || (!command.AllowProcessing
                    && order.Status != OrderStatuses.Pending))
            {
                await transaction.RollbackAsync(cancellationToken);
                return PreparedCancellation.WithResult(
                    order.OrderId,
                    new RefundSettlementResult(
                        false,
                        false,
                        false,
                        command.AllowProcessing
                            ? $"Không thể hủy đơn đang ở trạng thái '{order.Status}'."
                            : "Đơn hàng đã được xử lý, không thể tự hủy lúc này.",
                        order.OrderId));
            }

            if (payment == null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new PreparedCancellation(
                    order.OrderId,
                    null,
                    order.TotalAmount ?? 0,
                    string.Empty,
                    string.Empty,
                    false,
                    false,
                    false,
                    null);
            }

            string paymentMethod = payment.PaymentMethod ?? string.Empty;
            bool isVnPay = string.Equals(
                paymentMethod,
                PaymentMethods.VnPay,
                StringComparison.OrdinalIgnoreCase);
            bool alreadyRefunded = payment.PaymentStatus
                == PaymentStatuses.Refunded;
            bool refundRequired = payment.PaymentStatus
                == PaymentStatuses.Paid
                || payment.PaymentStatus
                    == PaymentStatuses.AwaitingRefund;

            if (alreadyRefunded)
            {
                await transaction.CommitAsync(cancellationToken);
                return new PreparedCancellation(
                    order.OrderId,
                    payment.PaymentId,
                    order.TotalAmount ?? 0,
                    paymentMethod,
                    payment.PaymentDate?.ToString("yyyyMMddHHmmss")
                        ?? DateTime.Now.ToString("yyyyMMddHHmmss"),
                    false,
                    false,
                    true,
                    null);
            }

            if (!refundRequired || !isVnPay)
            {
                await transaction.CommitAsync(cancellationToken);
                return new PreparedCancellation(
                    order.OrderId,
                    payment.PaymentId,
                    order.TotalAmount ?? 0,
                    paymentMethod,
                    payment.PaymentDate?.ToString("yyyyMMddHHmmss")
                        ?? DateTime.Now.ToString("yyyyMMddHHmmss"),
                    false,
                    refundRequired,
                    false,
                    null);
            }

            PaymentTransactions? refundEvent =
                await _context.PaymentTransactions
                    .FirstOrDefaultAsync(
                        current => current.IdempotencyKey == eventKey,
                        cancellationToken);

            if (refundEvent != null)
            {
                if (refundEvent.Status
                    == PaymentEventStatuses.Processed)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return PreparedCancellation.WithResult(
                        order.OrderId,
                        new RefundSettlementResult(
                            false,
                            false,
                            true,
                            "Refund hủy đơn đã được provider xử lý nhưng trạng thái đơn chưa đồng bộ. Cần đối soát, không gọi lại provider.",
                            order.OrderId,
                            order.TotalAmount ?? 0,
                            refundEvent.ProviderTransactionId));
                }

                if (refundEvent.Status
                        == PaymentEventStatuses.Processing
                    || refundEvent.Status
                        == PaymentEventStatuses.Received
                    || refundEvent.Status
                        == PaymentEventStatuses.RequiresReview)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return PreparedCancellation.WithResult(
                        order.OrderId,
                        new RefundSettlementResult(
                            false,
                            false,
                            true,
                            "Yêu cầu hoàn tiền khi hủy đơn đang xử lý hoặc cần đối soát. Không gửi lại lệnh VNPAY.",
                            order.OrderId,
                            order.TotalAmount ?? 0,
                            refundEvent.ProviderTransactionId));
                }

                refundEvent.Provider = PaymentMethods.VnPay;
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
                        Provider = PaymentMethods.VnPay,
                        EventType = PaymentEventTypes.Refund,
                        IdempotencyKey = eventKey,
                        Amount = order.TotalAmount,
                        Status = PaymentEventStatuses.Processing,
                        ReceivedAt = DateTime.Now
                    });
            }

            payment.PaymentStatus =
                PaymentStatuses.AwaitingRefund;
            payment.LastProcessedAt = DateTime.Now;
            payment.FailureReason = null;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new PreparedCancellation(
                order.OrderId,
                payment.PaymentId,
                order.TotalAmount ?? 0,
                paymentMethod,
                payment.PaymentDate?.ToString("yyyyMMddHHmmss")
                    ?? DateTime.Now.ToString("yyyyMMddHHmmss"),
                true,
                false,
                false,
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

    private async Task<RefundSettlementResult> FinalizeCancellationAsync(
        OrderCancellationCommand command,
        string eventKey,
        PreparedCancellation prepared,
        RefundProviderResult? providerResult,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var order = await _context.Orders
                .Include(current => current.Payments)
                .FirstOrDefaultAsync(
                    current => current.OrderId == command.OrderId
                        && (!command.CustomerId.HasValue
                            || current.CustomerId == command.CustomerId.Value),
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Không tìm thấy đơn hàng khi chốt hủy.");

            var payment = order.Payments
                .OrderByDescending(current => current.PaymentId)
                .FirstOrDefault();

            if (order.Status == OrderStatuses.Cancelled)
            {
                await transaction.CommitAsync(cancellationToken);
                return new RefundSettlementResult(
                    true,
                    true,
                    payment?.PaymentStatus
                        == PaymentStatuses.AwaitingRefund,
                    "Đơn hàng đã được hủy trước đó.",
                    order.OrderId,
                    order.TotalAmount ?? 0,
                    payment?.ProviderTransactionId);
            }

            bool canCancel = OrderStatuses.CanTransition(
                order.Status,
                OrderStatuses.Cancelled);
            if (!canCancel
                || (!command.AllowProcessing
                    && order.Status != OrderStatuses.Pending))
            {
                throw new InvalidOperationException(
                    "Trạng thái đơn đã thay đổi trong lúc xử lý hủy.");
            }

            DateTime now = DateTime.Now;

            await _shippingLifecycleService.SyncManualOrderStatusAsync(
                order.OrderId,
                OrderStatuses.Cancelled,
                now,
                cancellationToken);

            bool restored =
                await _orderInventoryService.RestoreOrderStockAsync(
                    order.OrderId,
                    command.RequestedBy == "Customer"
                        ? "Hoàn kho do khách hàng hủy đơn"
                        : "Hoàn kho do admin hủy đơn",
                    restoreFlashSaleSlots: true,
                    occurredAt: now,
                    cancellationToken: cancellationToken);

            order.CancellationReason = command.Reason.Trim();
            order.CancellationRequestedBy =
                string.IsNullOrWhiteSpace(command.RequestedBy)
                    ? "System"
                    : command.RequestedBy.Trim();

            bool requiresManualReview = false;
            string refundNote = string.Empty;

            if (payment != null)
            {
                if (providerResult != null
                    || prepared.PaymentAlreadyRefunded)
                {
                    payment.PaymentStatus =
                        PaymentStatuses.Refunded;
                    payment.LastProcessedAt = now;
                    payment.FailureReason = null;

                    PaymentTransactions? refundEvent =
                        await _context.PaymentTransactions
                            .FirstOrDefaultAsync(
                                current => current.IdempotencyKey
                                    == eventKey,
                                cancellationToken);

                    if (providerResult != null)
                    {
                        payment.LastResponseCode =
                            providerResult.ResponseCode;
                        payment.LastTransactionStatus =
                            providerResult.TransactionStatus;

                        if (refundEvent == null)
                        {
                            refundEvent = new PaymentTransactions
                            {
                                PaymentId = payment.PaymentId,
                                OrderId = order.OrderId,
                                EventType = PaymentEventTypes.Refund,
                                IdempotencyKey = eventKey,
                                Amount = order.TotalAmount,
                                ReceivedAt = now
                            };
                            _context.PaymentTransactions.Add(
                                refundEvent);
                        }

                        refundEvent.Status =
                            PaymentEventStatuses.Processed;
                        ApplyProviderResult(
                            refundEvent,
                            providerResult,
                            now);
                    }

                    string? refundReference =
                        providerResult?.TransactionReference
                        ?? payment.ProviderTransactionId;
                    refundNote =
                        " Đã hoàn tiền VNPAY"
                        + (string.IsNullOrWhiteSpace(refundReference)
                            ? "."
                            : $". Mã GD hoàn: {refundReference}.");
                }
                else if (prepared.RequiresManualRefund)
                {
                    requiresManualReview = true;
                    payment.PaymentStatus =
                        PaymentStatuses.AwaitingRefund;
                    payment.LastProcessedAt = now;
                    payment.FailureReason =
                        "Đơn đã hủy và cần hoàn tiền thủ công.";

                    PaymentTransactions? refundEvent =
                        await _context.PaymentTransactions
                            .FirstOrDefaultAsync(
                                current => current.IdempotencyKey
                                    == eventKey,
                                cancellationToken);

                    if (refundEvent == null)
                    {
                        refundEvent = new PaymentTransactions
                        {
                            PaymentId = payment.PaymentId,
                            OrderId = order.OrderId,
                            Provider = payment.PaymentMethod
                                ?? "MANUAL",
                            EventType = PaymentEventTypes.Refund,
                            IdempotencyKey = eventKey,
                            Amount = order.TotalAmount,
                            ReceivedAt = now
                        };
                        _context.PaymentTransactions.Add(refundEvent);
                    }

                    refundEvent.Status =
                        PaymentEventStatuses.RequiresReview;
                    refundEvent.ResponseCode =
                        "MANUAL_REQUIRED";
                    refundEvent.TransactionStatus =
                        "PENDING";
                    refundEvent.ProcessedAt = null;
                    refundEvent.ErrorMessage =
                        "Đơn đã hủy; cần hoàn tiền thủ công và nhập mã giao dịch khi đối soát.";

                    refundNote =
                        " Đơn đã thu tiền và đang chờ hoàn tiền thủ công.";
                }
                else if (payment.PaymentStatus
                    != PaymentStatuses.Refunded)
                {
                    payment.PaymentStatus =
                        PaymentStatuses.Cancelled;
                    payment.LastProcessedAt = now;
                    payment.FailureReason = null;
                }
            }

            string stockNote = restored
                ? " Đã hoàn kho và hoàn suất Flash Sale nếu có."
                : " Tồn kho đã được hoàn trước đó hoặc đơn chưa từng trừ kho.";
            string transitionNote =
                $"[{order.CancellationRequestedBy} hủy] Lý do: {order.CancellationReason}."
                + refundNote
                + stockNote;

            _orderStateService.Transition(
                order,
                OrderStatuses.Cancelled,
                transitionNote,
                now);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new RefundSettlementResult(
                true,
                false,
                requiresManualReview,
                requiresManualReview
                    ? "Đã hủy đơn và hoàn kho. Khoản thanh toán đang chờ hoàn tiền thủ công."
                    : "Hủy đơn hàng thành công.",
                order.OrderId,
                order.TotalAmount ?? 0,
                providerResult?.TransactionReference
                    ?? payment?.ProviderTransactionId);
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

    private sealed record PreparedCancellation(
        int OrderId,
        int? PaymentId,
        decimal Amount,
        string PaymentMethod,
        string TransactionDate,
        bool RequiresVnPayRefund,
        bool RequiresManualRefund,
        bool PaymentAlreadyRefunded,
        RefundSettlementResult? CompletedResult)
    {
        public static PreparedCancellation WithResult(
            int orderId,
            RefundSettlementResult result) =>
            new(
                orderId,
                null,
                result.RefundedAmount,
                string.Empty,
                string.Empty,
                false,
                false,
                false,
                result);
    }
}
