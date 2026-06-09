using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Shipping
{
    public int ShippingId { get; set; }

    public int? OrderId { get; set; }

    public string? Carrier { get; set; }

    public string? TrackingNumber { get; set; }

    public DateTime? ShippedDate { get; set; }

    public DateTime? EstimatedDelivery { get; set; }

    public DateTime? DeliveredDate { get; set; }

    public string? Status { get; set; }

    public string? Note { get; set; }

    public virtual Orders? Order { get; set; }
}
