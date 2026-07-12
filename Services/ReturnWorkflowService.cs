using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class ReturnWorkflowService
    : IReturnWorkflowService
{
    private const string LegacyReturnOrderStatus =
        "Trả hàng/Hoàn tiền";

    private readonly ApplicationDbContext _context;
    private readonly IOrderStateService _orderStateService;
    private readonly ILogger<ReturnWorkflowService> _logger;

    public ReturnWorkflowService(
        ApplicationDbContext context,
        IOrderStateService orderStateService,
        ILogger<ReturnWorkflowService> logger)
    {
        _context = context;
        _orderStateService = orderStateService;
        _logger = logger;
    }

    public async Task<ReturnWorkflowResult> TransitionAsync(
        ReturnWorkflowCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.ReturnId <= 0)
        {
            return Failure(
                "Mã hồ sơ trả hàng không hợp lệ.");
        }

        string targetStatus =
            NormalizeTargetStatus(
                command.NewStatus);

        if (string.IsNullOrWhiteSpace(targetStatus))
        {
            return Failure(
                "Trạng thái trả hàng không được hỗ trợ.");
        }

        string note;
        try
        {
            note = NormalizeAdminNote(
                command.AdminNote,
                targetStatus);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(ex.Message);
        }

        string actor = NormalizeActor(
            command.Actor);

        await using var transaction =
            await _context.Database
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

        try
        {
            OrderReturns? returnRequest =
                await _context.OrderReturns
                    .Include(current =>
                        current.Order)
                        .ThenInclude(order =>
                            order!.Payments)
                    .Include(current =>
                        current.Order)
                        .ThenInclude(order =>
                            order!.OrderHistories)
                    .Include(current =>
                        current.Customer)
                        .ThenInclude(customer =>
                            customer!.Account)
                    .FirstOrDefaultAsync(
                        current =>
                            current.ReturnId
                                == command.ReturnId,
                        cancellationToken);

            if (returnRequest?.Order == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    "Không tìm thấy hồ sơ trả hàng.");
            }

            Orders order = returnRequest.Order;
            Payments? payment = order.Payments
                .OrderByDescending(current =>
                    current.PaymentId)
                .FirstOrDefault();

            if (payment == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    "Không tìm thấy thông tin thanh toán của đơn hàng.");
            }

            string currentReturnStatus =
                returnRequest.Status?.Trim()
                ?? string.Empty;

            bool hasActiveRefundEvent =
                await HasActiveRefundEventAsync(
                    returnRequest.ReturnId,
                    order.OrderId,
                    cancellationToken);

            if (payment.PaymentStatus
                    == PaymentStatuses.Refunded
                && currentReturnStatus
                    != ReturnStatuses.Refunded)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    "Payment đã ở trạng thái hoàn tiền nhưng hồ sơ trả hàng chưa đồng bộ. Cần đối soát trước khi cập nhật.");
            }

            bool paymentStateRepaired =
                RepairLegacyPaymentState(
                    payment,
                    hasActiveRefundEvent,
                    currentReturnStatus);

            bool orderStateRepaired =
                SynchronizeCurrentOrderState(
                    order,
                    currentReturnStatus,
                    actor,
                    DateTime.Now);

            if (string.Equals(
                    currentReturnStatus,
                    targetStatus,
                    StringComparison.Ordinal))
            {
                if (paymentStateRepaired
                    || orderStateRepaired)
                {
                    await _context.SaveChangesAsync(
                        cancellationToken);
                }

                await transaction.CommitAsync(
                    cancellationToken);

                return BuildResult(
                    returnRequest,
                    true,
                    "Trạng thái đã được cập nhật trước đó.",
                    ReturnWorkflowNotificationKinds.None,
                    paymentStateRepaired,
                    orderStateRepaired);
            }

            if (!ReturnStatuses.CanTransition(
                    currentReturnStatus,
                    targetStatus))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    $"Không thể chuyển hồ sơ từ "
                    + $"'{currentReturnStatus}' "
                    + $"sang '{targetStatus}'.");
            }

            if (hasActiveRefundEvent)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    "Hồ sơ đã có lệnh hoàn tiền đang xử lý, đã hoàn tất hoặc cần đối soát. Không thể đổi tiến trình thủ công.");
            }

            if (targetStatus
                    == ReturnStatuses.Rejected
                && payment.PaymentStatus
                    == PaymentStatuses.AwaitingRefund)
            {
                // Chỉ có thể xảy ra với dữ liệu legacy không còn refund event.
                payment.PaymentStatus =
                    PaymentStatuses.Paid;
                payment.PaymentDate ??=
                    DateTime.Now;
                payment.FailureReason = null;
                payment.LastProcessedAt =
                    DateTime.Now;
                paymentStateRepaired = true;
            }

            DateTime now = DateTime.Now;
            string orderTargetStatus;
            string historyNote;
            string notificationKind;

            if (targetStatus
                == ReturnStatuses.AwaitingCustomer)
            {
                orderTargetStatus =
                    OrderStatuses.ReturnAwaitingCustomer;
                historyNote =
                    "Admin chấp nhận yêu cầu trả hàng. "
                    + $"Ghi chú: {note}. "
                    + $"Người xử lý: {actor}.";
                notificationKind =
                    ReturnWorkflowNotificationKinds.Accepted;
            }
            else if (targetStatus
                == ReturnStatuses.Inspecting)
            {
                orderTargetStatus =
                    OrderStatuses.ReturnInspecting;
                historyNote =
                    "Kho xác nhận đã nhận sản phẩm hoàn "
                    + "và bắt đầu kiểm định. "
                    + $"Ghi chú: {note}. "
                    + $"Người xử lý: {actor}.";
                notificationKind =
                    ReturnWorkflowNotificationKinds.Inspecting;
            }
            else
            {
                orderTargetStatus =
                    OrderStatuses.Completed;
                historyNote =
                    "Yêu cầu trả hàng bị từ chối. "
                    + $"Lý do: {note}. "
                    + $"Người xử lý: {actor}.";
                notificationKind =
                    ReturnWorkflowNotificationKinds.Rejected;
                returnRequest.ResolvedAt = now;

                if (string.Equals(
                        payment.PaymentMethod,
                        PaymentMethods.Cod,
                        StringComparison.OrdinalIgnoreCase)
                    && payment.PaymentStatus
                        != PaymentStatuses.Refunded)
                {
                    payment.PaymentStatus =
                        PaymentStatuses.Paid;
                    payment.PaymentDate ??= now;
                }
            }

            returnRequest.Status = targetStatus;
            returnRequest.AdminNote = note;
            returnRequest.IsAlertAdminRead = true;

            _orderStateService.Transition(
                order,
                orderTargetStatus,
                historyNote,
                now);

            await _context.SaveChangesAsync(
                cancellationToken);
            await transaction.CommitAsync(
                cancellationToken);

            return BuildResult(
                returnRequest,
                false,
                "Đã cập nhật tiến trình trả hàng.",
                notificationKind,
                paymentStateRepaired,
                orderStateRepaired);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            _logger.LogWarning(
                ex,
                "Xung đột khi cập nhật ReturnId {ReturnId}.",
                command.ReturnId);

            return Failure(
                "Hồ sơ vừa được tiến trình khác cập nhật. Vui lòng tải lại trang.");
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return Failure(ex.Message);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            _logger.LogError(
                ex,
                "Không thể cập nhật ReturnId {ReturnId}.",
                command.ReturnId);

            return Failure(
                "Không thể cập nhật tiến trình trả hàng. Vui lòng thử lại hoặc kiểm tra log hệ thống.");
        }
    }

    private async Task<bool>
        HasActiveRefundEventAsync(
            int returnId,
            int orderId,
            CancellationToken cancellationToken)
    {
        string eventKey =
            $"REFUND:RETURN:{returnId}";

        return await _context.PaymentTransactions
            .AsNoTracking()
            .AnyAsync(
                current =>
                    current.OrderId == orderId
                    && current.EventType
                        == PaymentEventTypes.Refund
                    && (current.IdempotencyKey
                            == eventKey
                        || current.IdempotencyKey
                            .StartsWith(
                                $"REFUND:RETURN:{returnId}"))
                    && current.Status
                        != PaymentEventStatuses.Failed
                    && current.Status
                        != PaymentEventStatuses.Rejected,
                cancellationToken);
    }

    private static bool RepairLegacyPaymentState(
        Payments payment,
        bool hasActiveRefundEvent,
        string currentReturnStatus)
    {
        if (payment.PaymentStatus
            != PaymentStatuses.AwaitingRefund)
        {
            return false;
        }

        if (hasActiveRefundEvent)
        {
            throw new InvalidOperationException(
                "Payment đang chờ hoàn tiền và đã có refund event. Không tự sửa trạng thái; cần tiếp tục đối soát hoặc hoàn tiền.");
        }

        if (currentReturnStatus
            == ReturnStatuses.Refunded)
        {
            throw new InvalidOperationException(
                "Hồ sơ đã hoàn tiền nhưng payment chưa ở trạng thái Refunded. Cần đối soát.");
        }

        payment.PaymentStatus =
            PaymentStatuses.Paid;
        payment.PaymentDate ??= DateTime.Now;
        payment.LastProcessedAt = DateTime.Now;
        payment.FailureReason = null;

        return true;
    }

    private bool SynchronizeCurrentOrderState(
        Orders order,
        string currentReturnStatus,
        string actor,
        DateTime now)
    {
        string? expectedStatus =
            ExpectedOrderStatus(
                currentReturnStatus);

        if (string.IsNullOrWhiteSpace(
                expectedStatus)
            || string.Equals(
                order.Status,
                expectedStatus,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!CanRepairOrderStatus(
                currentReturnStatus,
                order.Status))
        {
            throw new InvalidOperationException(
                $"Trạng thái hồ sơ '{currentReturnStatus}' "
                + $"không đồng bộ với trạng thái đơn "
                + $"'{order.Status}'. Không thể tự sửa an toàn.");
        }

        string previous =
            order.Status?.Trim()
            ?? string.Empty;

        order.Status = expectedStatus;

        _context.OrderHistories.Add(
            new OrderHistory
            {
                OrderId = order.OrderId,
                Status = expectedStatus,
                UpdatedAt = now,
                Note =
                    "Đồng bộ trạng thái legacy trước khi "
                    + "xử lý trả hàng. "
                    + $"Từ '{previous}' sang "
                    + $"'{expectedStatus}'. "
                    + $"Người xử lý: {actor}."
            });

        return true;
    }

    private static string? ExpectedOrderStatus(
        string returnStatus) =>
        returnStatus switch
        {
            ReturnStatuses.Pending =>
                OrderStatuses.ReturnPending,
            ReturnStatuses.AwaitingCustomer =>
                OrderStatuses.ReturnAwaitingCustomer,
            ReturnStatuses.Inspecting =>
                OrderStatuses.ReturnInspecting,
            ReturnStatuses.Rejected =>
                OrderStatuses.Completed,
            ReturnStatuses.Refunded =>
                OrderStatuses.Returned,
            _ => null
        };

    private static bool CanRepairOrderStatus(
        string returnStatus,
        string? orderStatus)
    {
        string current =
            orderStatus?.Trim()
            ?? string.Empty;

        if (current == LegacyReturnOrderStatus)
        {
            return true;
        }

        return returnStatus switch
        {
            ReturnStatuses.Pending =>
                current == OrderStatuses.Delivered
                || current
                    == OrderStatuses.ReturnPending,
            ReturnStatuses.AwaitingCustomer =>
                current
                    == OrderStatuses.ReturnPending
                || current
                    == OrderStatuses.ReturnAwaitingCustomer,
            ReturnStatuses.Inspecting =>
                current
                    == OrderStatuses.ReturnAwaitingCustomer
                || current
                    == OrderStatuses.ReturnInspecting,
            ReturnStatuses.Rejected =>
                current == OrderStatuses.Completed,
            ReturnStatuses.Refunded =>
                current == OrderStatuses.Returned,
            _ => false
        };
    }

    private static string NormalizeTargetStatus(
        string? value)
    {
        string normalized =
            value?.Trim()
            ?? string.Empty;

        return normalized switch
        {
            ReturnStatuses.AwaitingCustomer =>
                ReturnStatuses.AwaitingCustomer,
            ReturnStatuses.Inspecting =>
                ReturnStatuses.Inspecting,
            ReturnStatuses.Rejected =>
                ReturnStatuses.Rejected,
            _ => string.Empty
        };
    }

    private static string NormalizeAdminNote(
        string? value,
        string targetStatus)
    {
        string normalized =
            value?.Trim()
            ?? string.Empty;

        if (targetStatus
                == ReturnStatuses.Rejected
            && normalized.Length < 3)
        {
            throw new InvalidOperationException(
                "Vui lòng nhập lý do từ chối có ít nhất 3 ký tự.");
        }

        if (normalized.Length > 500)
        {
            throw new InvalidOperationException(
                "Ghi chú Admin vượt quá 500 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return targetStatus
                == ReturnStatuses.Inspecting
                    ? "Kho đã nhận hàng và bắt đầu kiểm định."
                    : "Yêu cầu đã được bộ phận phụ trách xác nhận.";
        }

        return normalized;
    }

    private static string NormalizeActor(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "admin";
        }

        string normalized = value.Trim();

        return normalized.Length <= 120
            ? normalized
            : normalized[..120];
    }

    private static ReturnWorkflowResult BuildResult(
        OrderReturns request,
        bool alreadyApplied,
        string message,
        string notificationKind,
        bool paymentStateRepaired,
        bool orderStateRepaired) =>
        new(
            true,
            alreadyApplied,
            message,
            request.ReturnId,
            request.OrderId,
            request.Customer?.Account?.Email,
            request.Customer?.FullName,
            notificationKind,
            paymentStateRepaired,
            orderStateRepaired);

    private static ReturnWorkflowResult Failure(
        string message) =>
        new(
            false,
            false,
            message);
}
