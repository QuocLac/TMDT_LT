using System;
using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed record ShippingCreationResult(
    bool Created,
    string ProviderCode,
    string? TrackingNumber,
    string Message);

public sealed record ShippingWebhookResult(
    bool Success,
    bool Duplicate,
    int? OrderId,
    string Message);

public interface IShippingLifecycleService
{
    Task<ShippingCreationResult> EnsureShipmentCreatedAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task SyncManualOrderStatusAsync(
        int orderId,
        string orderStatus,
        DateTime? occurredAt = null,
        CancellationToken cancellationToken = default);

    Task<ShippingWebhookResult> ProcessGhnWebhookAsync(
        string rawPayload,
        CancellationToken cancellationToken = default);
}
