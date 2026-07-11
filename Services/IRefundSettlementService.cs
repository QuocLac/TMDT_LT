using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed record ReturnRefundCommand(
    int ReturnId,
    bool IsProductIntact,
    string? AdminNote,
    string? ManualTransactionReference,
    string Actor);

public sealed record OrderCancellationCommand(
    int OrderId,
    string Reason,
    string RequestedBy,
    string Actor,
    int? CustomerId = null,
    bool AllowProcessing = false);

public sealed record RefundReconciliationCommand(
    long PaymentTransactionId,
    bool ProviderConfirmedSuccess,
    bool? IsProductIntact,
    string? AdminNote,
    string? TransactionReference,
    string Actor);

public sealed record RefundSettlementResult(
    bool Success,
    bool AlreadyProcessed,
    bool RequiresReview,
    string Message,
    int? OrderId = null,
    decimal RefundedAmount = 0,
    string? TransactionReference = null,
    string? CustomerEmail = null,
    string? CustomerName = null);

public interface IRefundSettlementService
{
    Task<RefundSettlementResult> SettleReturnAsync(
        ReturnRefundCommand command,
        CancellationToken cancellationToken = default);

    Task<RefundSettlementResult> SettleCancellationAsync(
        OrderCancellationCommand command,
        CancellationToken cancellationToken = default);

    Task<RefundSettlementResult> ReconcileRefundAsync(
        RefundReconciliationCommand command,
        CancellationToken cancellationToken = default);
}
