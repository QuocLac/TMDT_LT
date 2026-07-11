using System;

namespace TMDT_LT.Models;

public sealed class PaymentTransactions
{
    public long PaymentTransactionId { get; set; }

    public int PaymentId { get; set; }

    public int OrderId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? ProviderTransactionId { get; set; }

    public decimal? Amount { get; set; }

    public string Status { get; set; } = PaymentEventStatuses.Received;

    public string? ResponseCode { get; set; }

    public string? TransactionStatus { get; set; }

    public string? PayloadHash { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    public DateTime? ProcessedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public Payments Payment { get; set; } = null!;

    public Orders Order { get; set; } = null!;
}
