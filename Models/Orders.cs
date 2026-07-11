using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Orders
{
    public int OrderId { get; set; }

    public int? CustomerId { get; set; }

    public DateTime? OrderDate { get; set; }

    public string? Status { get; set; }

    public decimal? TotalAmount { get; set; }

    public string? ShippingFullName { get; set; }

    public string? ShippingPhone { get; set; }

    public string? ShippingStreet { get; set; }

    public string? ShippingDistrict { get; set; }

    public string? ShippingCity { get; set; }

    public string? ShippingCountry { get; set; }

    public bool IsStockDeducted { get; set; } = false;

    public DateTime? StockDeductedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<OrderDetails> OrderDetails { get; set; } = new List<OrderDetails>();

    public virtual ICollection<Payments> Payments { get; set; } = new List<Payments>();

    public virtual ICollection<PaymentTransactions> PaymentTransactions { get; set; } = new List<PaymentTransactions>();

    public virtual ICollection<OrderReservations> OrderReservations { get; set; } = new List<OrderReservations>();

    public virtual ICollection<Shipping> Shipping { get; set; } = new List<Shipping>();

    public virtual ICollection<OrderHistory> OrderHistories { get; set; } = new List<OrderHistory>();

    public virtual ICollection<OrderReturns> OrderReturns { get; set; } = new List<OrderReturns>();

    public string? CancellationReason { get; set; }

    public string? CancellationRequestedBy { get; set; }

    public DateTime? CompletedDate { get; set; }
}
