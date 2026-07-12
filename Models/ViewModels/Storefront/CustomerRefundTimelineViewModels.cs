using System;
using System.Collections.Generic;

namespace TMDT_LT.Models.ViewModels.Storefront;

public sealed class CustomerRefundTimelineEventViewModel
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string StatusCode { get; init; } = "processing";

    public DateTime OccurredAt { get; init; }
}

public sealed class CustomerRefundTimelineViewModel
{
    public bool Exists { get; init; }

    public int OrderId { get; init; }

    public string RefundType { get; init; } = string.Empty;

    public string StatusCode { get; init; } = "processing";

    public string StatusText { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string PaymentMethod { get; init; } = string.Empty;

    public string Destination { get; init; } = string.Empty;

    public string? TransactionReference { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public bool Completed { get; init; }

    public bool PollingRecommended { get; init; }

    public IReadOnlyList<CustomerRefundTimelineEventViewModel> Events { get; init; }
        = Array.Empty<CustomerRefundTimelineEventViewModel>();
}
