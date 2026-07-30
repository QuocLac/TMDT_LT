using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

/// <summary>
/// Tổng hợp số liệu tài chính vận hành từ đơn hàng, thanh toán, hoàn tiền,
/// giá vốn FIFO và chi phí giao hàng hiện có.
/// </summary>
public static class OrderProfitService
{
    public static async Task<OrderProfitOverview> GetOverviewAsync(
        ApplicationDbContext context,
        DateTime fromInclusive,
        DateTime toExclusive,
        CancellationToken cancellationToken = default)
    {
        /*
         * Một khoản tiền phải được đưa vào kỳ theo ngày thực thu, không chỉ theo
         * ngày tạo đơn. Đồng thời vẫn tải đơn được tạo trong kỳ để tính công nợ.
         */
        var orders = await context.Orders
            .AsNoTracking()
            .Include(order => order.Payments)
            .Include(order => order.PaymentTransactions)
            .Include(order => order.Shipping)
            .Include(order => order.OrderInventoryAllocations)
            .Where(order =>
                (order.OrderDate.HasValue
                    && order.OrderDate.Value >= fromInclusive
                    && order.OrderDate.Value < toExclusive)
                || (order.CompletedDate.HasValue
                    && order.CompletedDate.Value >= fromInclusive
                    && order.CompletedDate.Value < toExclusive)
                || order.Payments.Any(payment =>
                    payment.PaymentDate.HasValue
                    && payment.PaymentDate.Value >= fromInclusive
                    && payment.PaymentDate.Value < toExclusive)
                || order.PaymentTransactions.Any(transaction =>
                    (transaction.ProcessedAt.HasValue
                        && transaction.ProcessedAt.Value >= fromInclusive
                        && transaction.ProcessedAt.Value < toExclusive)
                    || (!transaction.ProcessedAt.HasValue
                        && transaction.ReceivedAt >= fromInclusive
                        && transaction.ReceivedAt < toExclusive)))
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

        foreach (Orders order in orders)
        {
            decimal orderInvoice = GetInvoiceTotal(order);
            bool orderCreatedInPeriod = IsInPeriod(
                order.OrderDate,
                fromInclusive,
                toExclusive);
            bool isTerminalWithoutRevenue = IsTerminalWithoutRevenue(order.Status);

            DateTime? revenueRecognitionDate = GetRevenueRecognitionDate(order);
            bool isRevenueRecognizedInPeriod =
                IsRevenueRecognized(order.Status)
                && IsInPeriod(
                    revenueRecognitionDate,
                    fromInclusive,
                    toExclusive);

            decimal orderPaidInPeriod = GetPaidAmountInPeriod(
                order,
                fromInclusive,
                toExclusive);
            decimal orderRefundInPeriod = Math.Min(
                GetPaidAmount(order),
                GetSuccessfulRefundAmountInPeriod(
                    order,
                    fromInclusive,
                    toExclusive));
            decimal orderNetCashInPeriod =
                orderPaidInPeriod - orderRefundInPeriod;

            decimal lifetimePaid = GetPaidAmount(order);
            decimal lifetimeRefund = Math.Min(
                lifetimePaid,
                GetSuccessfulRefundAmount(order));
            decimal lifetimeNetCash = Math.Max(
                0m,
                lifetimePaid - lifetimeRefund);

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
                    .Sum(shipping => Math.Max(
                        0m,
                        shipping.ShippingFee ?? 0m))
                : 0m;

            /*
             * Lợi nhuận tiền thu chỉ mang theo phần VAT và giá vốn tương ứng
             * với tỷ lệ tiền thực tế thu trong kỳ. Hoàn tiền lớn hơn tiền thu
             * của cùng kỳ được phản ánh thành dòng tiền âm, không bị ép về 0.
             */
            decimal positiveCashInPeriod = Math.Max(
                0m,
                orderNetCashInPeriod);
            decimal collectionRatio = orderInvoice > 0m
                ? Math.Clamp(
                    positiveCashInPeriod / orderInvoice,
                    0m,
                    1m)
                : 0m;
            decimal orderVatCollected = RoundMoney(
                Math.Max(0m, order.TaxAmount) * collectionRatio);
            decimal cashMatchedCogs = RoundMoney(
                currentAllocationCogs * collectionRatio);
            decimal cashMatchedShipping = positiveCashInPeriod > 0m
                ? orderShippingCostProxy
                : 0m;
            decimal orderCashProfit = orderNetCashInPeriod >= 0m
                ? RoundMoney(
                    orderNetCashInPeriod
                    - orderVatCollected
                    - cashMatchedCogs
                    - cashMatchedShipping)
                : RoundMoney(orderNetCashInPeriod);

            if (orderCreatedInPeriod && !isTerminalWithoutRevenue)
            {
                invoiceTotal += orderInvoice;
                outstandingAmount += Math.Max(
                    0m,
                    orderInvoice - lifetimeNetCash);
            }

            paidAmount += orderPaidInPeriod;
            refundAmount += orderRefundInPeriod;
            netCashCollected += orderNetCashInPeriod;
            outputVatCollected += orderVatCollected;
            cashContributionProfit += orderCashProfit;

            if (isRevenueRecognizedInPeriod)
            {
                recognizedOrderCount++;
                activeCogs += currentAllocationCogs;
                shippingCostProxy += orderShippingCostProxy;

                decimal recognizedNetRevenue = Math.Max(
                    0m,
                    order.MerchandiseNetRevenueAmount);
                decimal recognizedCogs = currentAllocationCogs > 0m
                    ? currentAllocationCogs
                    : Math.Max(0m, order.CogsAmount);
                recognizedGrossProfit += RoundMoney(
                    recognizedNetRevenue - recognizedCogs);

                if (order.CostCalculatedAt.HasValue)
                {
                    costedOrderCount++;
                }
            }
            else if (orderCreatedInPeriod && !isTerminalWithoutRevenue)
            {
                excludedPendingOrderCount++;
            }

            if (orderPaidInPeriod > 0m)
            {
                paidOrderCount++;
            }
            else if (orderCreatedInPeriod
                && orderInvoice > 0m
                && !isTerminalWithoutRevenue
                && lifetimeNetCash <= 0m)
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
            "Lợi nhuận tiền thu chưa trừ phí cổng thanh toán. Chi phí giao hàng hiện là số liệu vận hành có sẵn, chưa phải hóa đơn đối soát cuối cùng.");
    }

    private static decimal GetInvoiceTotal(Orders order) =>
        Math.Max(
            0m,
            order.GrandTotalAmount > 0m
                ? order.GrandTotalAmount
                : order.TotalAmount ?? 0m);

    private static decimal GetPaidAmount(Orders order)
    {
        decimal invoice = GetInvoiceTotal(order);
        decimal paid = order.Payments
            .Where(payment => IsPaidStatus(payment.PaymentStatus))
            .Sum(payment => ResolvePaidAmount(payment, invoice));

        return invoice > 0m
            ? Math.Min(paid, invoice)
            : paid;
    }

    private static decimal GetPaidAmountInPeriod(
        Orders order,
        DateTime fromInclusive,
        DateTime toExclusive)
    {
        decimal invoice = GetInvoiceTotal(order);
        DateTime? fallbackDate = GetRevenueRecognitionDate(order)
            ?? order.OrderDate;

        decimal paid = order.Payments
            .Where(payment => IsPaidStatus(payment.PaymentStatus))
            .Where(payment => IsInPeriod(
                payment.PaymentDate ?? fallbackDate,
                fromInclusive,
                toExclusive))
            .Sum(payment => ResolvePaidAmount(payment, invoice));

        return invoice > 0m
            ? Math.Min(paid, invoice)
            : paid;
    }

    private static decimal ResolvePaidAmount(
        Payments payment,
        decimal invoice)
    {
        decimal storedAmount = Math.Max(
            0m,
            payment.Amount ?? 0m);

        /*
         * Dữ liệu đơn cũ có thể đã được đánh dấu Paid nhưng Amount chưa được
         * backfill. Khi trạng thái thanh toán đã hợp lệ, dùng tổng hóa đơn làm
         * giá trị dự phòng thay vì biến khoản thu thành 0 đồng.
         */
        return storedAmount > 0m
            ? storedAmount
            : invoice;
    }

    private static decimal GetSuccessfulRefundAmount(Orders order) =>
        order.PaymentTransactions
            .Where(transaction =>
                IsRefundEvent(transaction.EventType)
                && IsSuccessfulTransactionStatus(transaction.Status))
            .Sum(transaction => Math.Max(
                0m,
                transaction.Amount ?? 0m));

    private static decimal GetSuccessfulRefundAmountInPeriod(
        Orders order,
        DateTime fromInclusive,
        DateTime toExclusive) =>
        order.PaymentTransactions
            .Where(transaction =>
                IsRefundEvent(transaction.EventType)
                && IsSuccessfulTransactionStatus(transaction.Status)
                && IsInPeriod(
                    transaction.ProcessedAt ?? transaction.ReceivedAt,
                    fromInclusive,
                    toExclusive))
            .Sum(transaction => Math.Max(
                0m,
                transaction.Amount ?? 0m));

    private static DateTime? GetRevenueRecognitionDate(Orders order)
    {
        if (order.CompletedDate.HasValue)
        {
            return order.CompletedDate.Value;
        }

        DateTime? deliveredAt = order.Shipping
            .Where(shipping => shipping.DeliveredDate.HasValue)
            .Select(shipping => shipping.DeliveredDate)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        return deliveredAt ?? order.OrderDate;
    }

    private static bool IsInPeriod(
        DateTime? value,
        DateTime fromInclusive,
        DateTime toExclusive) =>
        value.HasValue
        && value.Value >= fromInclusive
        && value.Value < toExclusive;

    private static bool IsRevenueRecognized(string? status) =>
        string.Equals(
            status,
            OrderStatuses.Delivered,
            StringComparison.Ordinal)
        || string.Equals(
            status,
            OrderStatuses.Completed,
            StringComparison.Ordinal)
        || string.Equals(
            status,
            OrderStatuses.ReturnPending,
            StringComparison.Ordinal)
        || string.Equals(
            status,
            OrderStatuses.ReturnAwaitingCustomer,
            StringComparison.Ordinal)
        || string.Equals(
            status,
            OrderStatuses.ReturnInspecting,
            StringComparison.Ordinal);

    private static bool IsTerminalWithoutRevenue(string? status) =>
        string.Equals(
            status,
            OrderStatuses.Cancelled,
            StringComparison.Ordinal)
        || string.Equals(
            status,
            OrderStatuses.Returned,
            StringComparison.Ordinal);

    private static bool HasCarrierCostBeenIncurred(Orders order) =>
        order.Shipping.Any(shipping =>
            !IsCancelledShipping(
                shipping.Status,
                shipping.ProviderStatus)
            && (shipping.ShippedDate.HasValue
                || shipping.DeliveredDate.HasValue
                || string.Equals(
                    shipping.Status,
                    ShippingStatuses.Created,
                    StringComparison.Ordinal)
                || string.Equals(
                    shipping.Status,
                    ShippingStatuses.Picking,
                    StringComparison.Ordinal)
                || string.Equals(
                    shipping.Status,
                    ShippingStatuses.InTransit,
                    StringComparison.Ordinal)
                || string.Equals(
                    shipping.Status,
                    ShippingStatuses.Delivered,
                    StringComparison.Ordinal)
                || string.Equals(
                    shipping.Status,
                    ShippingStatuses.Returning,
                    StringComparison.Ordinal)
                || string.Equals(
                    shipping.Status,
                    ShippingStatuses.Returned,
                    StringComparison.Ordinal)));

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
        return normalized.Contains(
                "refund",
                StringComparison.Ordinal)
            || normalized.Contains(
                "hoan tien",
                StringComparison.Ordinal);
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
        string combined =
            Normalize(status) + " " + Normalize(providerStatus);
        return combined.Contains(
                "cancel",
                StringComparison.Ordinal)
            || combined.Contains(
                "huy",
                StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant()
            .Replace("đ", "d", StringComparison.Ordinal)
            .Replace("á", "a", StringComparison.Ordinal)
            .Replace("à", "a", StringComparison.Ordinal)
            .Replace("ả", "a", StringComparison.Ordinal)
            .Replace("ã", "a", StringComparison.Ordinal)
            .Replace("ạ", "a", StringComparison.Ordinal)
            .Replace("ă", "a", StringComparison.Ordinal)
            .Replace("ắ", "a", StringComparison.Ordinal)
            .Replace("ằ", "a", StringComparison.Ordinal)
            .Replace("ẳ", "a", StringComparison.Ordinal)
            .Replace("ẵ", "a", StringComparison.Ordinal)
            .Replace("ặ", "a", StringComparison.Ordinal)
            .Replace("â", "a", StringComparison.Ordinal)
            .Replace("ấ", "a", StringComparison.Ordinal)
            .Replace("ầ", "a", StringComparison.Ordinal)
            .Replace("ẩ", "a", StringComparison.Ordinal)
            .Replace("ẫ", "a", StringComparison.Ordinal)
            .Replace("ậ", "a", StringComparison.Ordinal)
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("è", "e", StringComparison.Ordinal)
            .Replace("ẻ", "e", StringComparison.Ordinal)
            .Replace("ẽ", "e", StringComparison.Ordinal)
            .Replace("ẹ", "e", StringComparison.Ordinal)
            .Replace("ê", "e", StringComparison.Ordinal)
            .Replace("ế", "e", StringComparison.Ordinal)
            .Replace("ề", "e", StringComparison.Ordinal)
            .Replace("ể", "e", StringComparison.Ordinal)
            .Replace("ễ", "e", StringComparison.Ordinal)
            .Replace("ệ", "e", StringComparison.Ordinal)
            .Replace("í", "i", StringComparison.Ordinal)
            .Replace("ì", "i", StringComparison.Ordinal)
            .Replace("ỉ", "i", StringComparison.Ordinal)
            .Replace("ĩ", "i", StringComparison.Ordinal)
            .Replace("ị", "i", StringComparison.Ordinal)
            .Replace("ó", "o", StringComparison.Ordinal)
            .Replace("ò", "o", StringComparison.Ordinal)
            .Replace("ỏ", "o", StringComparison.Ordinal)
            .Replace("õ", "o", StringComparison.Ordinal)
            .Replace("ọ", "o", StringComparison.Ordinal)
            .Replace("ô", "o", StringComparison.Ordinal)
            .Replace("ố", "o", StringComparison.Ordinal)
            .Replace("ồ", "o", StringComparison.Ordinal)
            .Replace("ổ", "o", StringComparison.Ordinal)
            .Replace("ỗ", "o", StringComparison.Ordinal)
            .Replace("ộ", "o", StringComparison.Ordinal)
            .Replace("ơ", "o", StringComparison.Ordinal)
            .Replace("ớ", "o", StringComparison.Ordinal)
            .Replace("ờ", "o", StringComparison.Ordinal)
            .Replace("ở", "o", StringComparison.Ordinal)
            .Replace("ỡ", "o", StringComparison.Ordinal)
            .Replace("ợ", "o", StringComparison.Ordinal)
            .Replace("ú", "u", StringComparison.Ordinal)
            .Replace("ù", "u", StringComparison.Ordinal)
            .Replace("ủ", "u", StringComparison.Ordinal)
            .Replace("ũ", "u", StringComparison.Ordinal)
            .Replace("ụ", "u", StringComparison.Ordinal)
            .Replace("ư", "u", StringComparison.Ordinal)
            .Replace("ứ", "u", StringComparison.Ordinal)
            .Replace("ừ", "u", StringComparison.Ordinal)
            .Replace("ử", "u", StringComparison.Ordinal)
            .Replace("ữ", "u", StringComparison.Ordinal)
            .Replace("ự", "u", StringComparison.Ordinal)
            .Replace("ý", "y", StringComparison.Ordinal)
            .Replace("ỳ", "y", StringComparison.Ordinal)
            .Replace("ỷ", "y", StringComparison.Ordinal)
            .Replace("ỹ", "y", StringComparison.Ordinal)
            .Replace("ỵ", "y", StringComparison.Ordinal);
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
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
