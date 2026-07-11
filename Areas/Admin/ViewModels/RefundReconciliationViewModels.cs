using System;
using System.Collections.Generic;

namespace TMDT_LT.Areas.Admin.ViewModels;

public sealed class RefundReconciliationIndexViewModel
{
    public string StatusFilter { get; init; } = string.Empty;
    public string ScopeFilter { get; init; } = string.Empty;
    public string Search { get; init; } = string.Empty;
    public int RequiresReviewCount { get; init; }
    public int ProcessingCount { get; init; }
    public int FailedCount { get; init; }
    public int ProcessedCount { get; init; }
    public IReadOnlyList<RefundReconciliationItemViewModel> Items { get; init; }
        = Array.Empty<RefundReconciliationItemViewModel>();
}

public sealed class RefundReconciliationItemViewModel
{
    public long PaymentTransactionId { get; init; }
    public int OrderId { get; init; }
    public int PaymentId { get; init; }
    public int? ReturnId { get; init; }
    public string Scope { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string EventStatus { get; init; } = string.Empty;
    public string? ResponseCode { get; init; }
    public string? TransactionStatus { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime ReceivedAt { get; init; }
    public DateTime? ProcessedAt { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public string OrderStatus { get; init; } = string.Empty;
    public string? ReturnStatus { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CancellationReason { get; init; }
    public string? CancellationRequestedBy { get; init; }
    public bool RequiresManualReference { get; init; }
    public bool RequiresProductCondition { get; init; }
    public bool CanResolve { get; init; }
}
