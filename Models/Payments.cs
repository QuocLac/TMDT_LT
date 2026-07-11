using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public partial class Payments
{
    public int PaymentId { get; set; }

    public int? OrderId { get; set; }

    public string? PaymentMethod { get; set; }

    // Thời điểm thanh toán thực sự được xác nhận.
    public DateTime? PaymentDate { get; set; }

    public string? PaymentStatus { get; set; }

    public decimal? Amount { get; set; }

    public string Currency { get; set; } = "VND";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? ProviderTransactionId { get; set; }

    public string? LastResponseCode { get; set; }

    public string? LastTransactionStatus { get; set; }

    public DateTime? LastProcessedAt { get; set; }

    public string? FailureReason { get; set; }

    public virtual Orders? Order { get; set; }

    public virtual ICollection<PaymentTransactions> PaymentTransactions { get; set; } = new List<PaymentTransactions>();
}
