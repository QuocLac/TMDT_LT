using System;
using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed record PaymentEventClaim(
    int PaymentId,
    int OrderId,
    string Provider,
    string EventType,
    string IdempotencyKey,
    string? ProviderTransactionId,
    decimal? Amount,
    string? ResponseCode,
    string? TransactionStatus,
    string? RawPayload,
    DateTime? ReceivedAt = null);

public interface IPaymentTransactionService
{
    Task<bool> TryClaimAsync(
        PaymentEventClaim claim,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        string idempotencyKey,
        string status,
        string? errorMessage = null,
        DateTime? processedAt = null,
        CancellationToken cancellationToken = default);
}
