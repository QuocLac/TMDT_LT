using System;

namespace TMDT_LT.Models;

public sealed class ShippingEvents
{
    public long ShippingEventId { get; set; }

    public int ShippingId { get; set; }

    public int OrderId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? TrackingNumber { get; set; }

    public string? ProviderStatus { get; set; }

    public string? MappedStatus { get; set; }

    public string? PayloadHash { get; set; }

    public string Status { get; set; } = ShippingEventStatuses.Received;

    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    public DateTime? ProcessedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public Shipping Shipping { get; set; } = null!;

    public Orders Order { get; set; } = null!;
}
