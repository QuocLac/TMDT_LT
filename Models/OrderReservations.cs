using System;

namespace TMDT_LT.Models;

public sealed class OrderReservations
{
    public long ReservationId { get; set; }

    public int OrderId { get; set; }

    public int VariantId { get; set; }

    public int Quantity { get; set; }

    public string Status { get; set; } = OrderReservationStatuses.Reserved;

    public DateTime ReservedAt { get; set; } = DateTime.Now;

    public DateTime? ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public string? Reason { get; set; }

    public Orders Order { get; set; } = null!;

    public ProductVariants Variant { get; set; } = null!;
}
