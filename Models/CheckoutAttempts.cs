using System;

namespace TMDT_LT.Models;

public sealed class CheckoutAttempts
{
    public long CheckoutAttemptId { get; set; }

    public int CustomerId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public string Status { get; set; } = CheckoutAttemptStatuses.Processing;

    public int? OrderId { get; set; }

    public int SelectedAddressId { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public string? FailureReason { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public static class CheckoutAttemptStatuses
{
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public static class CheckoutIdempotencyConstants
{
    public const string FormFieldName = "CheckoutIdempotencyKey";

    public const string KeyItemName =
        "__TMDT_CheckoutIdempotencyKey";

    public const string VoucherItemName =
        "__TMDT_CheckoutVoucherCode";

    public const string CreatedOrderIdItemName =
        "__TMDT_CheckoutCreatedOrderId";
}
