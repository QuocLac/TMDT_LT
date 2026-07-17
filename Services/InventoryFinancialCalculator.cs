using System;
using System.Collections.Generic;
using System.Linq;

namespace TMDT_LT.Services;

/// <summary>
/// Quy tắc tiền tệ dùng chung cho nhập kho và xuất kho.
/// Tất cả phép tính đều dùng decimal và làm tròn tiền ở 2 chữ số.
/// </summary>
public static class InventoryFinancialCalculator
{
    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal NormalizeTaxRate(decimal value)
    {
        if (value > 1m && value <= 100m)
        {
            value /= 100m;
        }

        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Thuế suất phải nằm trong khoảng từ 0 đến 1 (hoặc 0 đến 100 nếu nhập theo phần trăm).");
        }

        return value;
    }

    public static PurchaseCostSummary CalculatePurchaseCosts(
        IReadOnlyCollection<PurchaseCostInput> items,
        decimal shippingFee,
        decimal otherFee,
        bool inputVatDeductible)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Phiếu nhập không có mặt hàng hợp lệ.");
        }

        shippingFee = Math.Max(0m, shippingFee);
        otherFee = Math.Max(0m, otherFee);

        var normalized = items
            .Select(item => item with
            {
                TaxRate = NormalizeTaxRate(item.TaxRate)
            })
            .ToList();

        if (normalized.Any(item => item.Quantity <= 0 || item.ImportPrice <= 0m))
        {
            throw new InvalidOperationException("Số lượng và giá nhập phải lớn hơn 0.");
        }

        decimal goodsSubtotal = normalized.Sum(item =>
            item.Quantity * item.ImportPrice);

        if (goodsSubtotal <= 0m)
        {
            throw new InvalidOperationException("Tổng tiền hàng nhập phải lớn hơn 0.");
        }

        decimal totalInputVat = normalized.Sum(item =>
            RoundMoney(item.Quantity * item.ImportPrice * item.TaxRate));

        decimal allocatableFees = shippingFee + otherFee;
        decimal allocatedFeeRunning = 0m;
        var lines = new List<PurchaseCostLine>();

        for (int index = 0; index < normalized.Count; index++)
        {
            var item = normalized[index];
            decimal lineBase = item.Quantity * item.ImportPrice;
            decimal lineVat = RoundMoney(lineBase * item.TaxRate);

            decimal lineAllocatedFees;
            if (index == normalized.Count - 1)
            {
                lineAllocatedFees = RoundMoney(
                    allocatableFees - allocatedFeeRunning);
            }
            else
            {
                lineAllocatedFees = RoundMoney(
                    allocatableFees * (lineBase / goodsSubtotal));
                allocatedFeeRunning += lineAllocatedFees;
            }

            decimal capitalizedVat = inputVatDeductible
                ? 0m
                : lineVat;
            decimal capitalizedLineCost =
                lineBase + capitalizedVat + lineAllocatedFees;
            decimal landedUnitCost = RoundMoney(
                capitalizedLineCost / item.Quantity);

            lines.Add(new PurchaseCostLine(
                item.VariantId,
                item.Quantity,
                item.ImportPrice,
                item.TaxRate,
                RoundMoney(lineBase),
                lineVat,
                lineAllocatedFees,
                landedUnitCost,
                RoundMoney(capitalizedLineCost)));
        }

        decimal inventoryCapitalizedCost = lines.Sum(line =>
            line.CapitalizedLineCost);
        decimal supplierPayable =
            goodsSubtotal + totalInputVat + shippingFee + otherFee;

        return new PurchaseCostSummary(
            RoundMoney(goodsSubtotal),
            RoundMoney(totalInputVat),
            RoundMoney(shippingFee),
            RoundMoney(otherFee),
            RoundMoney(supplierPayable),
            RoundMoney(inventoryCapitalizedCost),
            inputVatDeductible,
            lines);
    }

    public static SalesFinancialSummary CalculateSalesFinancials(
        IReadOnlyCollection<SalesLineInput> items,
        decimal discountPercent,
        decimal shippingCost,
        decimal cogs)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Phiếu xuất không có mặt hàng hợp lệ.");
        }

        discountPercent = Math.Clamp(discountPercent, 0m, 100m);
        shippingCost = Math.Max(0m, shippingCost);
        cogs = Math.Max(0m, cogs);
        decimal discountRate = discountPercent / 100m;

        var lines = new List<SalesFinancialLine>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0 || item.UnitPrice <= 0m)
            {
                throw new InvalidOperationException(
                    "Số lượng và giá xuất phải lớn hơn 0.");
            }

            decimal taxRate = NormalizeTaxRate(item.TaxRate);
            decimal grossBeforeTax = RoundMoney(
                item.Quantity * item.UnitPrice);
            decimal discountAmount = RoundMoney(
                grossBeforeTax * discountRate);
            decimal netBeforeTax = RoundMoney(
                grossBeforeTax - discountAmount);
            decimal outputVat = RoundMoney(
                netBeforeTax * taxRate);

            lines.Add(new SalesFinancialLine(
                item.VariantId,
                item.Quantity,
                item.UnitPrice,
                taxRate,
                grossBeforeTax,
                discountAmount,
                netBeforeTax,
                outputVat));
        }

        decimal grossSales = lines.Sum(line => line.GrossBeforeTax);
        decimal discount = lines.Sum(line => line.DiscountAmount);
        decimal netSales = lines.Sum(line => line.NetBeforeTax);
        decimal outputVatTotal = lines.Sum(line => line.OutputVat);
        decimal invoiceTotal = netSales + outputVatTotal;

        // VAT đầu ra là khoản phải nộp/đối trừ, không phải doanh thu hay lợi nhuận.
        // ShippingCost được hiểu là chi phí thực tế doanh nghiệp chịu, không phải phí thu của cửa hàng.
        decimal realizedAccountingProfit =
            netSales - cogs - shippingCost;
        decimal marginPercent = netSales > 0m
            ? realizedAccountingProfit / netSales * 100m
            : 0m;

        return new SalesFinancialSummary(
            RoundMoney(grossSales),
            RoundMoney(discount),
            RoundMoney(netSales),
            RoundMoney(outputVatTotal),
            RoundMoney(invoiceTotal),
            RoundMoney(cogs),
            RoundMoney(shippingCost),
            RoundMoney(realizedAccountingProfit),
            RoundMoney(marginPercent),
            lines);
    }
}

public sealed record PurchaseCostInput(
    int VariantId,
    int Quantity,
    decimal ImportPrice,
    decimal TaxRate);

public sealed record PurchaseCostLine(
    int VariantId,
    int Quantity,
    decimal ImportPrice,
    decimal TaxRate,
    decimal GoodsAmount,
    decimal InputVatAmount,
    decimal AllocatedFees,
    decimal LandedUnitCost,
    decimal CapitalizedLineCost);

public sealed record PurchaseCostSummary(
    decimal GoodsSubtotal,
    decimal InputVatAmount,
    decimal ShippingFee,
    decimal OtherFee,
    decimal SupplierPayable,
    decimal InventoryCapitalizedCost,
    bool InputVatDeductible,
    IReadOnlyList<PurchaseCostLine> Lines);

public sealed record SalesLineInput(
    int VariantId,
    int Quantity,
    decimal UnitPrice,
    decimal TaxRate);

public sealed record SalesFinancialLine(
    int VariantId,
    int Quantity,
    decimal UnitPrice,
    decimal TaxRate,
    decimal GrossBeforeTax,
    decimal DiscountAmount,
    decimal NetBeforeTax,
    decimal OutputVat);

public sealed record SalesFinancialSummary(
    decimal GrossSalesBeforeTax,
    decimal DiscountAmount,
    decimal NetSalesBeforeTax,
    decimal OutputVatAmount,
    decimal InvoiceTotal,
    decimal Cogs,
    decimal ShippingCost,
    decimal RealizedAccountingProfit,
    decimal MarginPercent,
    IReadOnlyList<SalesFinancialLine> Lines);
