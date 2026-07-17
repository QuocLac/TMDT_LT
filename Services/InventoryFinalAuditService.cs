using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public static class InventoryFinalAuditService
{
    public static async Task<InventoryFinalAuditReport> AuditAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken = default)
    {
        var variants = await context.ProductVariants
            .AsNoTracking()
            .Select(item => new
            {
                item.VariantId,
                Stock = item.Stock ?? 0
            })
            .ToListAsync(cancellationToken);

        var lots = await context.InventoryLots
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var serials = await context.ProductSerials
            .AsNoTracking()
            .Select(item => new
            {
                item.SerialId,
                item.VariantId,
                item.LotId,
                item.OrderId,
                item.Status
            })
            .ToListAsync(cancellationToken);

        var allocations = await context.OrderInventoryAllocations
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var orders = await context.Orders
            .AsNoTracking()
            .Select(item => new
            {
                item.OrderId,
                item.Status,
                item.IsStockDeducted,
                item.MerchandiseNetRevenueAmount,
                item.CogsAmount,
                item.GrossProfitAmount,
                item.CostCalculatedAt
            })
            .ToListAsync(cancellationToken);

        var activeLots = lots
            .Where(lot => lot.IsActive && !lot.IsDeleted)
            .ToList();

        var lotStockByVariant = activeLots
            .GroupBy(lot => lot.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(lot => Math.Max(0, lot.RemainingQuantity)));

        var variantMismatches = variants
            .Where(item =>
                item.Stock != lotStockByVariant.GetValueOrDefault(item.VariantId))
            .Select(item => new InventoryVariantStockMismatch(
                item.VariantId,
                item.Stock,
                lotStockByVariant.GetValueOrDefault(item.VariantId)))
            .OrderBy(item => item.VariantId)
            .ToList();

        var inStockSerialsByLot = serials
            .Where(serial => string.Equals(
                serial.Status,
                "InStock",
                StringComparison.Ordinal))
            .GroupBy(serial => serial.LotId)
            .ToDictionary(group => group.Key, group => group.Count());

        var lotMismatches = activeLots
            .Where(lot =>
                lot.WarehouseId <= 0
                || lot.ReceivedQuantity < 0
                || lot.RemainingQuantity < 0
                || lot.RemainingQuantity > lot.ReceivedQuantity
                || inStockSerialsByLot.GetValueOrDefault(lot.LotId)
                    != lot.RemainingQuantity)
            .Select(lot => new InventoryLotAuditIssue(
                lot.LotId,
                lot.VariantId,
                lot.WarehouseId,
                lot.ReceivedQuantity,
                lot.RemainingQuantity,
                inStockSerialsByLot.GetValueOrDefault(lot.LotId),
                BuildLotIssue(lot, inStockSerialsByLot.GetValueOrDefault(lot.LotId))))
            .OrderBy(item => item.LotId)
            .ToList();

        var activeAllocations = allocations
            .Where(allocation =>
                allocation.Status == OrderInventoryAllocationStatuses.Consumed)
            .ToList();

        var soldSerialCounts = serials
            .Where(serial =>
                string.Equals(serial.Status, "Sold", StringComparison.Ordinal)
                && serial.OrderId.HasValue)
            .GroupBy(serial => (
                OrderId: serial.OrderId!.Value,
                serial.VariantId,
                serial.LotId))
            .ToDictionary(group => group.Key, group => group.Count());

        var allocationIssues = activeAllocations
            .GroupBy(allocation => new
            {
                allocation.OrderId,
                allocation.VariantId,
                allocation.LotId
            })
            .Select(group =>
            {
                int allocated = group.Sum(item => item.Quantity);
                int soldSerials = soldSerialCounts.GetValueOrDefault((
                    group.Key.OrderId,
                    group.Key.VariantId,
                    group.Key.LotId));
                return new InventoryAllocationAuditIssue(
                    group.Key.OrderId,
                    group.Key.VariantId,
                    group.Key.LotId,
                    allocated,
                    soldSerials);
            })
            .Where(item => item.AllocatedQuantity != item.SoldSerialQuantity)
            .OrderBy(item => item.OrderId)
            .ThenBy(item => item.LotId)
            .ToList();

        var allocationCogsByOrder = allocations
            .Where(allocation =>
                allocation.Status == OrderInventoryAllocationStatuses.Consumed
                || allocation.Status == OrderInventoryAllocationStatuses.Damaged)
            .GroupBy(allocation => allocation.OrderId)
            .ToDictionary(
                group => group.Key,
                group => RoundMoney(group.Sum(item => Math.Max(0m, item.TotalCost))));

        var financialIssues = orders
            .Where(order =>
            {
                decimal expectedCogs = allocationCogsByOrder
                    .GetValueOrDefault(order.OrderId);
                decimal expectedProfit = RoundMoney(
                    Math.Max(0m, order.MerchandiseNetRevenueAmount)
                    - expectedCogs);
                return Math.Abs(order.CogsAmount - expectedCogs) > 0.01m
                    || Math.Abs(order.GrossProfitAmount - expectedProfit) > 0.01m;
            })
            .Select(order =>
            {
                decimal expectedCogs = allocationCogsByOrder
                    .GetValueOrDefault(order.OrderId);
                return new InventoryOrderFinancialIssue(
                    order.OrderId,
                    order.Status ?? string.Empty,
                    order.CogsAmount,
                    expectedCogs,
                    order.GrossProfitAmount,
                    RoundMoney(
                        Math.Max(0m, order.MerchandiseNetRevenueAmount)
                        - expectedCogs));
            })
            .OrderBy(item => item.OrderId)
            .ToList();

        int deductedWithoutAllocation = orders.Count(order =>
            order.IsStockDeducted
            && !activeAllocations.Any(allocation =>
                allocation.OrderId == order.OrderId));

        int criticalIssueCount =
            lotMismatches.Count
            + allocationIssues.Count
            + deductedWithoutAllocation;

        return new InventoryFinalAuditReport(
            DateTime.Now,
            variants.Count,
            activeLots.Count,
            activeLots.Sum(lot => Math.Max(0, lot.RemainingQuantity)),
            variantMismatches,
            lotMismatches,
            allocationIssues,
            financialIssues,
            deductedWithoutAllocation,
            criticalIssueCount == 0
                && variantMismatches.Count == 0
                && financialIssues.Count == 0);
    }

    public static async Task<InventoryStockRepairResult>
        RepairVariantStockSnapshotsAsync(
            ApplicationDbContext context,
            int? accountId,
            string reason,
            CancellationToken cancellationToken = default)
    {
        var variants = await context.ProductVariants
            .ToListAsync(cancellationToken);
        var lotTotals = await context.InventoryLots
            .Where(lot => lot.IsActive && !lot.IsDeleted)
            .GroupBy(lot => lot.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(lot => lot.RemainingQuantity)
            })
            .ToDictionaryAsync(
                item => item.VariantId,
                item => item.Quantity,
                cancellationToken);

        int repaired = 0;
        foreach (var variant in variants)
        {
            int oldStock = variant.Stock ?? 0;
            int correctedStock = Math.Max(
                0,
                lotTotals.GetValueOrDefault(variant.VariantId));

            if (oldStock == correctedStock)
            {
                continue;
            }

            variant.Stock = correctedStock;
            repaired++;

            context.InventoryTransactions.Add(
                new InventoryTransactions
                {
                    VariantId = variant.VariantId,
                    TransactionType = "RECONCILE_STOCK_SNAPSHOT",
                    Quantity = 0,
                    TransactionDate = DateTime.Now,
                    AccountId = accountId,
                    Note =
                        $"{reason}. Đồng bộ ProductVariants.Stock "
                        + $"{oldStock} -> {correctedStock} theo tổng lô hoạt động; "
                        + "không phát sinh nhập/xuất vật lý."
                });
        }

        await context.SaveChangesAsync(cancellationToken);
        return new InventoryStockRepairResult(
            repaired,
            variants.Count);
    }

    private static string BuildLotIssue(
        InventoryLots lot,
        int inStockSerialCount)
    {
        var issues = new List<string>();
        if (lot.WarehouseId <= 0)
        {
            issues.Add("chưa gán kho");
        }
        if (lot.ReceivedQuantity < 0)
        {
            issues.Add("số lượng nhập âm");
        }
        if (lot.RemainingQuantity < 0)
        {
            issues.Add("tồn lô âm");
        }
        if (lot.RemainingQuantity > lot.ReceivedQuantity)
        {
            issues.Add("tồn lớn hơn số đã nhận");
        }
        if (inStockSerialCount != lot.RemainingQuantity)
        {
            issues.Add(
                $"serial InStock {inStockSerialCount} khác tồn {lot.RemainingQuantity}");
        }

        return string.Join("; ", issues);
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record InventoryVariantStockMismatch(
    int VariantId,
    int VariantStock,
    int LotStock);

public sealed record InventoryLotAuditIssue(
    int LotId,
    int VariantId,
    int WarehouseId,
    int ReceivedQuantity,
    int RemainingQuantity,
    int InStockSerialQuantity,
    string Issue);

public sealed record InventoryAllocationAuditIssue(
    int OrderId,
    int VariantId,
    int LotId,
    int AllocatedQuantity,
    int SoldSerialQuantity);

public sealed record InventoryOrderFinancialIssue(
    int OrderId,
    string Status,
    decimal SnapshotCogs,
    decimal AllocationCogs,
    decimal SnapshotProfit,
    decimal ExpectedProfit);

public sealed record InventoryFinalAuditReport(
    DateTime GeneratedAt,
    int VariantCount,
    int ActiveLotCount,
    int AvailableQuantity,
    IReadOnlyList<InventoryVariantStockMismatch> VariantStockMismatches,
    IReadOnlyList<InventoryLotAuditIssue> LotIssues,
    IReadOnlyList<InventoryAllocationAuditIssue> AllocationIssues,
    IReadOnlyList<InventoryOrderFinancialIssue> FinancialIssues,
    int DeductedOrdersWithoutAllocation,
    bool Healthy)
{
    public int CriticalIssueCount =>
        LotIssues.Count
        + AllocationIssues.Count
        + DeductedOrdersWithoutAllocation;
}

public sealed record InventoryStockRepairResult(
    int RepairedVariantCount,
    int ScannedVariantCount);
