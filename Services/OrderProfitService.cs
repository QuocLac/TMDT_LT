using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

/// <summary>
/// Báo cáo tài chính vận hành từ dữ liệu project hiện có.
/// - Lợi nhuận ghi nhận chỉ tính đơn đã giao/hoàn thành hoặc đang trong quy trình trả hàng.
/// - Lợi nhuận tiền thu tính trên phần tiền thực tế đã thu, sau hoàn tiền.
/// - Phí vận chuyển trong Shipping hiện là snapshot/proxy, chưa phải đối soát carrier invoice.
/// - Phí cổng thanh toán chưa có trường chuẩn hóa nên chưa được khấu trừ.
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
        decimal shippingCostProxy = 0m;
        decimal recognizedGrossProfit = 0m;
        decimal cashContributionProfit = 0m;
        int paidOrderCount = 0;
        int unpaidOrderCount = 0;
        int costedOrderCount = 0;
        int recognizedOrderCount = 0;
        int excludedPendingOrderCount = 0;

        foreach (var order in orders)
        {
            decimal orderInvoice = GetInvoiceTotal(order);
            decimal orderPaid = GetPaidAmount(order);
            decimal orderRefund = Math.Min(
                orderPaid,
                GetSuccessfulRefundAmount(order));
            decimal orderNetCash = Math.Max(0m, orderPaid - orderRefund);
            bool isTerminalWithoutRevenue = IsTerminalWithoutRevenue(order.Status);
            bool isRevenueRecognized = IsRevenueRecognized(order.Status);

            decimal collectionRatio = orderInvoice > 0m
                ? Math.Clamp(orderNetCash / orderInvoice, 0m, 1m)
                : 0m;
            decimal orderVatCollected = RoundMoney(
                Math.Max(0m, order.TaxAmount) * collectionRatio);

            decimal currentAllocationCogs = order.OrderInventoryAllocations
                .Where(allocation =>
                    allocation.Status == OrderInventoryAllocationStatuses.Consumed
                    || allocation.Status == OrderInventoryAllocationStatuses.Damaged)
                .Sum(allocation => Math.Max(0m, allocation.TotalCost));

            decimal orderShippingCostProxy = HasCarrierCostBeenIncurred(order)
                ? order.Shipping
                    .Where(shipping =>
                        !IsCancelledShipping(
                            shipping.Status,
                            shipping.ProviderStatus))
                    .Sum(shipping => Math.Max(0m, shipping.ShippingFee ?? 0m))
                : 0m;

            // Tiền thu chỉ mang theo phần giá vốn tương ứng với tỷ lệ thu tiền.
            // Tránh biến đơn chưa thanh toán/chưa giao thành "lợi nhuận tiền mặt âm".
            decimal cashMatchedCogs = RoundMoney(
                currentAllocationCogs * collectionRatio);
            decimal cashMatchedShipping = orderNetCash > 0m
                ? orderShippingCostProxy
                : 0m;
            decimal orderCashProfit = RoundMoney(
                orderNetCash
                - orderVatCollected
                - cashMatchedCogs
                - cashMatchedShipping);

            invoiceTotal += isTerminalWithoutRevenue ? 0m : orderInvoice;
            paidAmount += orderPaid;
            refundAmount += orderRefund;
            netCashCollected += orderNetCash;
            if (!isTerminalWithoutRevenue)
            {
                outstandingAmount += Math.Max(0m, orderInvoice - orderNetCash);
            }

            activeCogs += currentAllocationCogs;
            outputVatCollected += orderVatCollected;
            shippingCostProxy += orderShippingCostProxy;
            cashContributionProfit += orderCashProfit;

            if (order.CostCalculatedAt.HasValue)
            {
                costedOrderCount++;
            }

            if (isRevenueRecognized)
            {
                recognizedOrderCount++;
                decimal recognizedNetRevenue = Math.Max(
                    0m,
                    order.MerchandiseNetRevenueAmount);
                decimal recognizedCogs = currentAllocationCogs > 0m
                    ? currentAllocationCogs
                    : Math.Max(0m, order.CogsAmount);
                recognizedGrossProfit += RoundMoney(
                    recognizedNetRevenue - recognizedCogs);
            }
            else if (!isTerminalWithoutRevenue)
            {
                excludedPendingOrderCount++;
            }

            if (orderNetCash > 0m)
            {
                paidOrderCount++;
            }
            else if (orderInvoice > 0m && !isTerminalWithoutRevenue)
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
            RoundMoney(shippingCostProxy),
            RoundMoney(recognizedGrossProfit),
            RoundMoney(cashContributionProfit),
            recognizedOrderCount,
            excludedPendingOrderCount,
            "Lợi nhuận tiền thu chưa trừ phí cổng thanh toán. ShippingFee hiện là snapshot/proxy, chưa phải hóa đơn đối soát thực tế từ đơn vị vận chuyển.");
    }

    private static decimal GetInvoiceTotal(Orders order) =>
        Math.Max(
            0m,
            order.GrandTotalAmount > 0m
                ? order.GrandTotalAmount
                : order.TotalAmount ?? 0m);

    private static decimal GetPaidAmount(Orders order)
    {
        decimal paid = order.Payments
            .Where(payment => IsPaidStatus(payment.PaymentStatus))
            .Sum(payment => Math.Max(0m, payment.Amount ?? 0m));

        // Trong mô hình hiện tại một đơn thường chỉ có một payment thành công.
        // Cap theo tổng invoice để tránh payment retry bị cộng trùng trong dashboard.
        decimal invoice = GetInvoiceTotal(order);
        return invoice > 0m
            ? Math.Min(paid, invoice)
            : paid;
    }

    private static decimal GetSuccessfulRefundAmount(Orders order) =>
        order.PaymentTransactions
            .Where(transaction =>
                IsRefundEvent(transaction.EventType)
                && IsSuccessfulTransactionStatus(transaction.Status))
            .Sum(transaction => Math.Max(0m, transaction.Amount ?? 0m));

    private static bool IsRevenueRecognized(string? status) =>
        string.Equals(status, OrderStatuses.Delivered, StringComparison.Ordinal)
        || string.Equals(status, OrderStatuses.Completed, StringComparison.Ordinal)
        || string.Equals(status, OrderStatuses.ReturnPending, StringComparison.Ordinal)
        || string.Equals(status, OrderStatuses.ReturnAwaitingCustomer, StringComparison.Ordinal)
        || string.Equals(status, OrderStatuses.ReturnInspecting, StringComparison.Ordinal);

    private static bool IsTerminalWithoutRevenue(string? status) =>
        string.Equals(status, OrderStatuses.Cancelled, StringComparison.Ordinal)
        || string.Equals(status, OrderStatuses.Returned, StringComparison.Ordinal);

    private static bool HasCarrierCostBeenIncurred(Orders order) =>
        order.Shipping.Any(shipping =>
            !IsCancelledShipping(shipping.Status, shipping.ProviderStatus)
            && (shipping.ShippedDate.HasValue
                || shipping.DeliveredDate.HasValue
                || string.Equals(shipping.Status, ShippingStatuses.Created, StringComparison.Ordinal)
                || string.Equals(shipping.Status, ShippingStatuses.Picking, StringComparison.Ordinal)
                || string.Equals(shipping.Status, ShippingStatuses.InTransit, StringComparison.Ordinal)
                || string.Equals(shipping.Status, ShippingStatuses.Delivered, StringComparison.Ordinal)
                || string.Equals(shipping.Status, ShippingStatuses.Returning, StringComparison.Ordinal)
                || string.Equals(shipping.Status, ShippingStatuses.Returned, StringComparison.Ordinal)));

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
    int RecognizedOrderCount,
    int ExcludedPendingOrderCount,
    string Limitation);
