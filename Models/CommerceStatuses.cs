using System;
using System.Collections.Generic;

namespace TMDT_LT.Models;

public static class OrderStatuses
{
    public const string Pending = "Chờ xác nhận";
    public const string Processing = "Đang xử lý";
    public const string Shipping = "Đang giao";
    public const string Delivered = "Đã giao";
    public const string Completed = "Hoàn thành";
    public const string Cancelled = "Đã hủy";
    public const string ReturnPending = "Chờ duyệt";
    public const string ReturnAwaitingCustomer = "Chờ khách trả hàng";
    public const string ReturnInspecting = "Đang kiểm định";
    public const string Returned = "Đã hoàn trả";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedTransitions =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [Pending] = new(StringComparer.Ordinal) { Processing, Cancelled },
            [Processing] = new(StringComparer.Ordinal) { Shipping, Cancelled },
            [Shipping] = new(StringComparer.Ordinal) { Delivered },
            [Delivered] = new(StringComparer.Ordinal) { Completed, ReturnPending },
            [ReturnPending] = new(StringComparer.Ordinal) { ReturnAwaitingCustomer, Completed },
            [ReturnAwaitingCustomer] = new(StringComparer.Ordinal) { ReturnInspecting, Completed },
            [ReturnInspecting] = new(StringComparer.Ordinal) { Returned, Completed }
        };

    public static bool CanTransition(string? currentStatus, string? targetStatus)
    {
        if (string.IsNullOrWhiteSpace(currentStatus) || string.IsNullOrWhiteSpace(targetStatus))
        {
            return false;
        }

        return AllowedTransitions.TryGetValue(currentStatus.Trim(), out var targets)
            && targets.Contains(targetStatus.Trim());
    }

    public static bool IsTerminal(string? status) =>
        string.Equals(status, Cancelled, StringComparison.Ordinal)
        || string.Equals(status, Returned, StringComparison.Ordinal);
}

public static class PaymentStatuses
{
    public const string Unpaid = "Chưa thanh toán";
    public const string AwaitingGateway = "Chờ thanh toán qua Cổng";
    public const string AwaitingBankTransfer = "Chờ xác nhận chuyển khoản";
    public const string Paid = "Đã thanh toán";
    public const string AwaitingRefund = "Chờ hoàn tiền";
    public const string Refunded = "Đã hoàn tiền";
    public const string Failed = "Thất bại";
    public const string Cancelled = "Đã hủy";
}

public static class PaymentMethods
{
    public const string Cod = "COD";
    public const string VnPay = "VNPAY";
    public const string BankTransfer = "BankTransfer";
}

public static class ReturnStatuses
{
    public const string Pending = "Chờ duyệt";
    public const string AwaitingCustomer = "Chờ khách trả hàng";
    public const string Inspecting = "Đang kiểm định";
    public const string Refunded = "Hoàn tiền thành công";
    public const string Rejected = "Đã từ chối";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedTransitions =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [Pending] = new(StringComparer.Ordinal) { AwaitingCustomer, Rejected },
            [AwaitingCustomer] = new(StringComparer.Ordinal) { Inspecting, Rejected },
            [Inspecting] = new(StringComparer.Ordinal) { Refunded, Rejected }
        };

    public static bool CanTransition(string? currentStatus, string? targetStatus)
    {
        if (string.IsNullOrWhiteSpace(currentStatus) || string.IsNullOrWhiteSpace(targetStatus))
        {
            return false;
        }

        return AllowedTransitions.TryGetValue(currentStatus.Trim(), out var targets)
            && targets.Contains(targetStatus.Trim());
    }
}

public static class PaymentEventStatuses
{
    public const string Received = "Received";
    public const string Processing = "Processing";
    public const string Processed = "Processed";
    public const string Ignored = "Ignored";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
    public const string RequiresReview = "RequiresReview";
}

public static class PaymentEventTypes
{
    public const string Created = "CREATED";
    public const string Ipn = "IPN";
    public const string Return = "RETURN";
    public const string BankWebhook = "BANK_WEBHOOK";
    public const string Refund = "REFUND";
    public const string Retry = "RETRY";
    public const string Expired = "EXPIRED";
    public const string CodCollected = "COD_COLLECTED";
}

public static class OrderReservationStatuses
{
    public const string Reserved = "Reserved";
    public const string Consumed = "Consumed";
    public const string Released = "Released";
    public const string Damaged = "Damaged";
}

public static class ShippingStatuses
{
    public const string Pending = "Chờ tạo vận đơn";
    public const string AwaitingManual = "Chờ tạo vận đơn thủ công";
    public const string Created = "Đã tạo vận đơn";
    public const string Picking = "Đang lấy hàng";
    public const string InTransit = "Đang vận chuyển";
    public const string Delivered = "Đã giao";
    public const string DeliveryFailed = "Giao thất bại";
    public const string Returning = "Đang hoàn hàng";
    public const string Returned = "Đã hoàn hàng";
    public const string Cancelled = "Đã hủy vận đơn";
    public const string Exception = "Sự cố vận chuyển";
    public const string Unknown = "Chưa xác định";
}

public static class ShippingEventStatuses
{
    public const string Received = "Received";
    public const string Processed = "Processed";
    public const string Ignored = "Ignored";
    public const string Rejected = "Rejected";
}

public static class ShippingEventTypes
{
    public const string Created = "CREATED";
    public const string Webhook = "WEBHOOK";
    public const string ManualSync = "MANUAL_SYNC";
    public const string Cancelled = "CANCELLED";
}
