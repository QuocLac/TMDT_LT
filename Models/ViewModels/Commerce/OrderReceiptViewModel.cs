using System;
using System.Collections.Generic;
using System.Linq;
using TMDT_LT.Models;

namespace TMDT_LT.Models.ViewModels.Commerce;

public sealed class OrderReceiptLineViewModel
{
    public string ProductName { get; init; } = string.Empty;

    public string VariantName { get; init; } = string.Empty;

    public string VariantCode { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal OriginalUnitPrice { get; init; }

    public decimal DiscountPerUnit { get; init; }

    public decimal LineTotal { get; init; }

    public bool IsFlashSaleItem { get; init; }
}

public sealed class OrderReceiptViewModel
{
    public string StoreName { get; init; } = "PHONE.ST";

    public string DocumentTitle { get; init; } = "Phiếu xác nhận đơn hàng";

    public string ReceiptNumber { get; init; } = string.Empty;

    public int OrderId { get; init; }

    public DateTime IssuedAt { get; init; }

    public DateTime? OrderDate { get; init; }

    public string OrderStatus { get; init; } = string.Empty;

    public string RecipientName { get; init; } = string.Empty;

    public string RecipientPhone { get; init; } = string.Empty;

    public string ShippingAddress { get; init; } = string.Empty;

    public string PaymentMethod { get; init; } = string.Empty;

    public string PaymentStatus { get; init; } = string.Empty;

    public DateTime? PaymentDate { get; init; }

    public string? ProviderTransactionId { get; init; }

    public string Carrier { get; init; } = string.Empty;

    public string ShippingStatus { get; init; } = string.Empty;

    public string? TrackingNumber { get; init; }

    public bool IsPaid { get; init; }

    public bool IsRefunded { get; init; }

    public bool IsAdminView { get; init; }

    public OrderFinancialSummaryViewModel Financial { get; init; }
        = new();

    public IReadOnlyList<OrderReceiptLineViewModel> Lines { get; init; }
        = Array.Empty<OrderReceiptLineViewModel>();

    public static OrderReceiptViewModel Create(
        Orders order,
        bool isAdminView)
    {
        Payments? payment = order.Payments
            .OrderByDescending(current => current.PaymentId)
            .FirstOrDefault();

        Shipping? shipment = order.Shipping
            .OrderByDescending(current => current.ShippingId)
            .FirstOrDefault();

        bool isPaid = string.Equals(
            payment?.PaymentStatus,
            PaymentStatuses.Paid,
            StringComparison.OrdinalIgnoreCase);

        bool isRefunded = string.Equals(
            payment?.PaymentStatus,
            PaymentStatuses.Refunded,
            StringComparison.OrdinalIgnoreCase);

        DateTime issuedAt = payment?.PaymentDate
            ?? order.OrderDate
            ?? DateTime.Now;

        string documentTitle = isRefunded
            ? "Biên nhận đơn hàng đã hoàn tiền"
            : isPaid
                ? "Biên nhận thanh toán đơn hàng"
                : "Phiếu xác nhận đơn hàng";

        var lines = order.OrderDetails
            .OrderBy(current => current.OrderDetailId)
            .Select(CreateLine)
            .ToArray();

        return new OrderReceiptViewModel
        {
            DocumentTitle = documentTitle,
            ReceiptNumber = $"ORD-{order.OrderId:D8}",
            OrderId = order.OrderId,
            IssuedAt = issuedAt,
            OrderDate = order.OrderDate,
            OrderStatus = order.Status ?? "Chưa xác định",
            RecipientName = order.ShippingFullName ?? string.Empty,
            RecipientPhone = order.ShippingPhone ?? string.Empty,
            ShippingAddress = BuildAddress(order),
            PaymentMethod = FriendlyPaymentMethod(payment?.PaymentMethod),
            PaymentStatus = payment?.PaymentStatus ?? "Chưa ghi nhận",
            PaymentDate = payment?.PaymentDate,
            ProviderTransactionId = payment?.ProviderTransactionId,
            Carrier = shipment?.Carrier ?? "Chưa phân công",
            ShippingStatus = shipment?.Status ?? ShippingStatuses.Pending,
            TrackingNumber = shipment?.TrackingNumber,
            IsPaid = isPaid,
            IsRefunded = isRefunded,
            IsAdminView = isAdminView,
            Financial = OrderFinancialSummaryViewModel.Create(order),
            Lines = lines
        };
    }

    private static OrderReceiptLineViewModel CreateLine(
        OrderDetails detail)
    {
        string productName = !string.IsNullOrWhiteSpace(
            detail.ProductNameSnapshot)
                ? detail.ProductNameSnapshot.Trim()
                : detail.Variant?.Product?.Name
                    ?? $"Sản phẩm #{detail.ProductIdSnapshot ?? detail.VariantId}";

        string variantName = !string.IsNullOrWhiteSpace(
            detail.VariantNameSnapshot)
                ? detail.VariantNameSnapshot.Trim()
                : string.Join(
                    " / ",
                    new[]
                    {
                        detail.Variant?.Color,
                        detail.Variant?.Ram,
                        detail.Variant?.Storage
                    }
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(value)));

        string variantCode = !string.IsNullOrWhiteSpace(
            detail.VariantCodeSnapshot)
                ? detail.VariantCodeSnapshot.Trim()
                : detail.VariantId.HasValue
                    ? $"VAR-{detail.VariantId.Value:D6}"
                    : "—";

        int quantity = Math.Max(0, detail.Quantity ?? 0);
        decimal unitPrice = Math.Max(0m, detail.UnitPrice ?? 0m);
        decimal originalUnitPrice = Math.Max(
            unitPrice,
            detail.OriginalUnitPrice ?? unitPrice);
        decimal discountPerUnit = Math.Max(
            0m,
            detail.DiscountAmountPerUnit
                ?? originalUnitPrice - unitPrice);
        decimal lineTotal = Math.Max(
            0m,
            detail.LineTotal ?? unitPrice * quantity);

        return new OrderReceiptLineViewModel
        {
            ProductName = productName,
            VariantName = variantName,
            VariantCode = variantCode,
            Quantity = quantity,
            UnitPrice = unitPrice,
            OriginalUnitPrice = originalUnitPrice,
            DiscountPerUnit = discountPerUnit,
            LineTotal = lineTotal,
            IsFlashSaleItem = detail.IsFlashSaleItem
        };
    }

    private static string BuildAddress(Orders order)
    {
        string[] parts =
        {
            order.ShippingStreet ?? string.Empty,
            order.ShippingWard ?? string.Empty,
            order.ShippingDistrict ?? string.Empty,
            order.ShippingCity ?? string.Empty,
            order.ShippingCountry ?? string.Empty
        };

        return string.Join(
            ", ",
            parts.Where(part =>
                !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim()));
    }

    private static string FriendlyPaymentMethod(
        string? method)
    {
        if (string.Equals(
            method,
            PaymentMethods.VnPay,
            StringComparison.OrdinalIgnoreCase))
        {
            return "VNPAY";
        }

        if (string.Equals(
            method,
            PaymentMethods.BankTransfer,
            StringComparison.OrdinalIgnoreCase))
        {
            return "Chuyển khoản ngân hàng";
        }

        if (string.Equals(
            method,
            PaymentMethods.Cod,
            StringComparison.OrdinalIgnoreCase))
        {
            return "Thanh toán khi nhận hàng";
        }

        return string.IsNullOrWhiteSpace(method)
            ? "Chưa xác định"
            : method.Trim();
    }
}
