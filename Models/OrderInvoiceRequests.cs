using System;

namespace TMDT_LT.Models;

public sealed class OrderInvoiceRequests
{
    public int InvoiceRequestId { get; set; }

    public int OrderId { get; set; }

    public int? CustomerId { get; set; }

    public string BuyerType { get; set; }
        = InvoiceBuyerTypes.Individual;

    public string BuyerName { get; set; } = string.Empty;

    public string? TaxCode { get; set; }

    public string BuyerAddress { get; set; } = string.Empty;

    public string BuyerEmail { get; set; } = string.Empty;

    public string? BuyerPhone { get; set; }

    public string Status { get; set; }
        = InvoiceRequestStatuses.Pending;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAt { get; set; }

    public string? ReviewedBy { get; set; }

    public string? AdminNote { get; set; }

    public DateTime? IssuedAt { get; set; }

    public string? InvoiceNumber { get; set; }

    public string? InvoiceLookupCode { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Orders? Order { get; set; }
}

public static class InvoiceBuyerTypes
{
    public const string Individual = "Individual";

    public const string Organization = "Organization";
}

public static class InvoiceRequestStatuses
{
    public const string Pending = "Pending";

    public const string Issued = "Issued";

    public const string Rejected = "Rejected";

    public const string Cancelled = "Cancelled";
}

public static class CheckoutInvoiceRequestConstants
{
    public const string SnapshotItemName =
        "__TMDT_CheckoutInvoiceRequest";
}

public sealed record CheckoutInvoiceRequestSnapshot(
    bool IsRequested,
    string BuyerType,
    string BuyerName,
    string? TaxCode,
    string BuyerAddress,
    string BuyerEmail,
    string? BuyerPhone)
{
    public static CheckoutInvoiceRequestSnapshot None { get; }
        = new(
            false,
            InvoiceBuyerTypes.Individual,
            string.Empty,
            null,
            string.Empty,
            string.Empty,
            null);

    public string CanonicalFingerprint =>
        string.Join(
            "|",
            IsRequested ? "1" : "0",
            Normalize(BuyerType),
            Normalize(BuyerName),
            Normalize(TaxCode),
            Normalize(BuyerAddress),
            Normalize(BuyerEmail),
            Normalize(BuyerPhone));

    private static string Normalize(string? value) =>
        value?.Trim().ToUpperInvariant()
        ?? string.Empty;
}

public sealed class CheckoutInvoiceValidationException
    : InvalidOperationException
{
    public CheckoutInvoiceValidationException(
        string message)
        : base(message)
    {
    }
}
