using System;

namespace TMDT_LT.Models.ViewModels.Commerce;

public sealed class CustomerInvoiceStatusViewModel
{
    public bool Exists { get; init; }

    public int OrderId { get; init; }

    public string StatusCode { get; init; } = "pending";

    public string StatusText { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string BuyerType { get; init; } = string.Empty;

    public string BuyerName { get; init; } = string.Empty;

    public string? TaxCode { get; init; }

    public string BuyerAddress { get; init; } = string.Empty;

    public string BuyerEmail { get; init; } = string.Empty;

    public string? BuyerPhone { get; init; }

    public DateTime RequestedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public string? InvoiceNumber { get; init; }

    public string? InvoiceLookupCode { get; init; }

    public string? InvoiceLookupUrl { get; init; }

    public string? RejectionReason { get; init; }

    public bool PollingRecommended { get; init; }
}
