using System;
using System.Linq;
using TMDT_LT.Models;

namespace TMDT_LT.Models.ViewModels.Commerce;

public sealed class OrderFinancialSummaryViewModel
{
    public int OrderId { get; init; }

    public decimal SubtotalAmount { get; init; }

    public decimal DiscountAmount { get; init; }

    public decimal ShippingFee { get; init; }

    public decimal TaxAmount { get; init; }

    public decimal GrandTotalAmount { get; init; }

    public string? AppliedVoucherCode { get; init; }

    public string SnapshotSource { get; init; } = string.Empty;

    public bool IsLegacyFallback { get; init; }

    public decimal ReconciliationDelta { get; init; }

    public bool IsConsistent { get; init; }

    public string? PaymentMethod { get; init; }

    public string? PaymentStatus { get; init; }

    public decimal? PaymentAmount { get; init; }

    public string? CheckoutReference { get; init; }

    public static OrderFinancialSummaryViewModel Create(
        Orders order)
    {
        decimal lineSubtotal = order.OrderDetails
            .Sum(detail =>
                (detail.LineTotal ?? 0m) > 0m
                    ? detail.LineTotal ?? 0m
                    : (detail.UnitPrice ?? 0m)
                        * (detail.Quantity ?? 0));

        decimal shippingFee = Math.Max(
            0m,
            order.ShippingFee
                ?? order.Shipping
                    .OrderByDescending(current =>
                        current.ShippingId)
                    .Select(current =>
                        current.ShippingFee)
                    .FirstOrDefault()
                ?? 0m);

        bool hasSnapshot =
            !string.IsNullOrWhiteSpace(
                order.CheckoutIdempotencyKey)
            || !string.IsNullOrWhiteSpace(
                order.AppliedVoucherCode)
            || order.SubtotalAmount != 0m
            || order.DiscountAmount != 0m
            || order.TaxAmount != 0m
            || order.GrandTotalAmount != 0m;

        decimal subtotal = hasSnapshot
            ? Math.Max(0m, order.SubtotalAmount)
            : Math.Max(0m, lineSubtotal);

        decimal tax = hasSnapshot
            ? Math.Max(0m, order.TaxAmount)
            : 0m;

        decimal grandTotal = hasSnapshot
            ? Math.Max(0m, order.GrandTotalAmount)
            : Math.Max(
                0m,
                order.TotalAmount
                    ?? lineSubtotal + shippingFee);

        decimal discount = hasSnapshot
            ? Math.Max(0m, order.DiscountAmount)
            : Math.Max(
                0m,
                subtotal
                    + shippingFee
                    + tax
                    - grandTotal);

        decimal calculatedGrandTotal =
            subtotal
            - discount
            + shippingFee
            + tax;

        decimal reconciliationDelta = RoundMoney(
            grandTotal - calculatedGrandTotal);

        Payments? payment = order.Payments
            .OrderByDescending(current =>
                current.PaymentId)
            .FirstOrDefault();

        return new OrderFinancialSummaryViewModel
        {
            OrderId = order.OrderId,
            SubtotalAmount = RoundMoney(subtotal),
            DiscountAmount = RoundMoney(discount),
            ShippingFee = RoundMoney(shippingFee),
            TaxAmount = RoundMoney(tax),
            GrandTotalAmount = RoundMoney(grandTotal),
            AppliedVoucherCode =
                discount > 0m
                    ? NormalizeCode(
                        order.AppliedVoucherCode)
                    : null,
            SnapshotSource = hasSnapshot
                ? "Snapshot checkout"
                : "Dữ liệu legacy được suy ra",
            IsLegacyFallback = !hasSnapshot,
            ReconciliationDelta =
                reconciliationDelta,
            IsConsistent =
                Math.Abs(reconciliationDelta)
                    <= 0.01m,
            PaymentMethod =
                payment?.PaymentMethod,
            PaymentStatus =
                payment?.PaymentStatus,
            PaymentAmount =
                payment?.Amount,
            CheckoutReference =
                MaskReference(
                    order.CheckoutIdempotencyKey)
        };
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);

    private static string? NormalizeCode(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant();
    }

    private static string? MaskReference(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        if (normalized.Length <= 14)
        {
            return normalized;
        }

        return $"{normalized[..8]}…{normalized[^4..]}";
    }
}
