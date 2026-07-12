using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public enum CheckoutClaimState
{
    Acquired,
    Completed,
    InProgress,
    Conflict
}

public sealed record CheckoutClaimCommand(
    int CustomerId,
    string IdempotencyKey,
    string RequestHash,
    int SelectedAddressId,
    string PaymentMethod);

public sealed record CheckoutClaimResult(
    CheckoutClaimState State,
    int? OrderId,
    string Message);

public interface ICheckoutIdempotencyService
{
    Task<CheckoutClaimResult> BeginAsync(
        CheckoutClaimCommand command,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(
        string idempotencyKey,
        int customerId,
        int orderId,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        string idempotencyKey,
        int customerId,
        string? error,
        CancellationToken cancellationToken = default);
}
