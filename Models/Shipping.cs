using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Shipping
{
    public int ShippingId { get; set; }

    public int? OrderId { get; set; }

    public string? Carrier { get; set; }

    public string? ProviderCode { get; set; }

    public string? TrackingNumber { get; set; }

    public string? ProviderStatus { get; set; }

    public DateTime? ShippedDate { get; set; }

    public DateTime? EstimatedDelivery { get; set; }

    public DateTime? DeliveredDate { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? Status { get; set; }

    public decimal? ShippingFee { get; set; }

    public decimal? CodAmount { get; set; }

    public decimal? InsuranceValue { get; set; }

    public int? ServiceTypeId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastWebhookAt { get; set; }

    public string? LastError { get; set; }

    public string? Note { get; set; }

    public virtual Orders? Order { get; set; }

    public virtual ICollection<ShippingEvents> ShippingEvents { get; set; } = new List<ShippingEvents>();
}
