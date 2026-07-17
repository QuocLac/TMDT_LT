using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

/// <summary>
/// Tính lợi nhuận thực nhận gần nhất có thể từ dữ liệu hiện có:
/// tiền thanh toán đã xác nhận - hoàn tiền - VAT đầu ra đã thu
/// - giá vốn allocation còn tiêu thụ - phí vận chuyển thực tế.
/// Phí cổng thanh toán chưa có bảng dữ liệu chuẩn nên được công bố là thiếu.
/// </summary>
public static class OrderProfitService
{
    public static async Task<OrderProfitOverview> GetOverviewAsync(
        ApplicationDbContext context,
        DateTime fromInclusive,
        DateTime toExclusive,
        CancellationToken cancellationToken = default)
    {
        var orders = await context.Orders
            .AsNoTracking()
            .Include(order => order.Payments)
            .Include(order => order.PaymentTransactions)
            .Include(order => order.Shipping)
            .Include(order => order.OrderInventoryAllocations)
            .Where(order =>
                order.OrderDate >= fromInclusive
                && order.OrderDate < toExclusive)
            .ToListAsync(cancellationToken);

        decimal invoiceTotal = 0m;
        decimal paidAmount = 0m;
        decimal refundAmount = 0m;
        decimal netCashCollected = 0m;
        decimal outstandingAmount = 0m;
        decimal activeCogs = 0m;
        decimal outputVatCollected = 0m;
        decimal carrierShippingExpense = 0m;
        decimal recognizedGrossProfit = 0m;
        decimal cashContributionProfit = 0m;
        int paidOrderCount = 0;
        int unpaidOrderCount = 0;
        int costedOrderCount = 0;

        foreach (var order in orders)
        {
            decimal orderInvoice = order.GrandTotalAmount > 0m
                ? order.GrandTotalAmount
                : order.TotalAmount ?? 0m;
            decimal orderPaid = order.Payments
                .Where(payment => IsPaidStatus(payment.PaymentStatus))
                .Sum(payment => payment.Amount ?? 0m);
            decimal orderRefund = order.PaymentTransactions
                .Where(transaction =>
                    IsRefundEvent(transaction.EventType)
                    && IsSuccessfulTransactionStatus(transaction.Status))
                .Sum(transaction => transaction.Amount ?? 0m);
            decimal orderNetCash = Math.Max(0m, orderPaid - orderRefund);
            decimal collectionRatio = orderInvoice > 0m
                ? Math.Clamp(orderNetCash / orderInvoice, 0m, 1m)
                : 0m;
            decimal orderVatCollected = RoundMoney(
                Math.Max(0m, order.TaxAmount) * collectionRatio);
            decimal orderCogs = order.OrderInventoryAllocations
                .Where(allocation =>
                    allocation.Status == OrderInventoryAllocationStatuses.Consumed
                    || allocation.Status == OrderInventoryAllocationStatuses.Damaged)
                .Sum(allocation => allocation.TotalCost);
            decimal shippingExpense = order.Shipping
                .Where(shipping => !IsCancelledShipping(shipping.Status, shipping.ProviderStatus))
                .Sum(shipping => shipping.ShippingFee ?? 0m);
            decimal orderCashProfit = RoundMoney(
                orderNetCash
                - orderVatCollected
                - orderCogs
                - shippingExpense);

            invoiceTotal += orderInvoice;
            paidAmount += orderPaid;
            refundAmount += orderRefund;
            netCashCollected += orderNetCash;
            outstandingAmount += Math.Max(0m, orderInvoice - orderNetCash);
            activeCogs += orderCogs;
            outputVatCollected += orderVatCollected;
            carrierShippingExpense += shippingExpense;
            cashContributionProfit += orderCashProfit;

            if (order.CostCalculatedAt.HasValue)
            {
                costedOrderCount++;
                recognizedGrossProfit += order.GrossProfitAmount;
            }

            if (orderNetCash > 0m)
            {
                paidOrderCount++;
            }
            else if (orderInvoice > 0m)
            {
                unpaidOrderCount++;
            }
        }

        return new OrderProfitOverview(
            fromInclusive,
            toExclusive,
            orders.Count,
            paidOrderCount,
            unpaidOrderCount,
            costedOrderCount,
            RoundMoney(invoiceTotal),
            RoundMoney(paidAmount),
            RoundMoney(refundAmount),
            RoundMoney(netCashCollected),
            RoundMoney(outstandingAmount),
            RoundMoney(activeCogs),
            RoundMoney(outputVatCollected),
            RoundMoney(carrierShippingExpense),
            RoundMoney(recognizedGrossProfit),
            RoundMoney(cashContributionProfit),
            "Lợi nhuận thực nhận tạm tính chưa trừ phí cổng thanh toán vì project chưa lưu ProviderFee/SettlementFee chuẩn hóa.");
    }

    private static bool IsPaidStatus(string? value)
    {
        string normalized = Normalize(value);
        return normalized is
            "paid"
            or "completed"
            or "success"
            or "succeeded"
            or "settled"
            or "da thanh toan";
    }

    private static bool IsRefundEvent(string? value)
    {
        string normalized = Normalize(value);
        return normalized.Contains("refund", StringComparison.Ordinal)
            || normalized.Contains("hoan tien", StringComparison.Ordinal);
    }

    private static bool IsSuccessfulTransactionStatus(string? value)
    {
        string normalized = Normalize(value);
        return normalized is
            "processed"
            or "completed"
            or "success"
            or "succeeded"
            or "settled";
    }

    private static bool IsCancelledShipping(
        string? status,
        string? providerStatus)
    {
        string combined = Normalize(status) + " " + Normalize(providerStatus);
        return combined.Contains("cancel", StringComparison.Ordinal)
            || combined.Contains("huy", StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant()
            .Replace("đ", "d", StringComparison.Ordinal)
            .Replace("ã", "a", StringComparison.Ordinal)
            .Replace("ạ", "a", StringComparison.Ordinal)
            .Replace("ấ", "a", StringComparison.Ordinal)
            .Replace("ầ", "a", StringComparison.Ordinal)
            .Replace("ậ", "a", StringComparison.Ordinal)
            .Replace("ă", "a", StringComparison.Ordinal)
            .Replace("ắ", "a", StringComparison.Ordinal)
            .Replace("ằ", "a", StringComparison.Ordinal)
            .Replace("ê", "e", StringComparison.Ordinal)
            .Replace("ế", "e", StringComparison.Ordinal)
            .Replace("ề", "e", StringComparison.Ordinal)
            .Replace("ệ", "e", StringComparison.Ordinal)
            .Replace("ô", "o", StringComparison.Ordinal)
            .Replace("ố", "o", StringComparison.Ordinal)
            .Replace("ồ", "o", StringComparison.Ordinal)
            .Replace("ơ", "o", StringComparison.Ordinal)
            .Replace("ớ", "o", StringComparison.Ordinal)
            .Replace("ờ", "o", StringComparison.Ordinal)
            .Replace("ư", "u", StringComparison.Ordinal)
            .Replace("ứ", "u", StringComparison.Ordinal)
            .Replace("ừ", "u", StringComparison.Ordinal)
            .Replace("ý", "y", StringComparison.Ordinal);
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record OrderProfitOverview(
    DateTime FromInclusive,
    DateTime ToExclusive,
    int OrderCount,
    int PaidOrderCount,
    int UnpaidOrderCount,
    int CostedOrderCount,
    decimal InvoiceTotal,
    decimal PaidAmount,
    decimal RefundAmount,
    decimal NetCashCollected,
    decimal OutstandingAmount,
    decimal ActiveCogs,
    decimal OutputVatCollected,
    decimal CarrierShippingExpense,
    decimal RecognizedGrossProfit,
    decimal CashContributionProfit,
    string Limitation);
