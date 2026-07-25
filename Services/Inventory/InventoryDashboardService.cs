using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;
using TMDT_LT.Services.Inventory.Contracts;

namespace TMDT_LT.Services.Inventory;

public sealed class InventoryDashboardService
{
    private const int LowStockThreshold = 5;
    private readonly ApplicationDbContext _context;

    public InventoryDashboardService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryDashboardResponse> GetOverviewAsync(
        int? warehouseId,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken = default)
    {
        DateTime fromInclusive = (fromDate ?? DateTime.Today.AddDays(-29)).Date;
        DateTime toInclusive = (toDate ?? DateTime.Today).Date;
        if (fromInclusive > toInclusive)
        {
            throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.");
        }
        if ((toInclusive - fromInclusive).TotalDays > 366)
        {
            throw new ArgumentException("Khoảng thời gian tối đa là 366 ngày.");
        }

        DateTime toExclusive = toInclusive.AddDays(1);
        Warehouses warehouse = await ResolveWarehouseAsync(warehouseId, cancellationToken);
        DateTime now = DateTime.Now;
        DateTime agedThreshold = now.AddDays(-90);

        var lotRows = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                && lot.IsActive
                && !lot.IsDeleted
                && lot.RemainingQuantity > 0)
            .Select(lot => new
            {
                lot.VariantId,
                lot.RemainingQuantity,
                lot.UnitCost,
                lot.ReceivedDate,
                ProductName = lot.Variant != null && lot.Variant.Product != null
                    ? lot.Variant.Product.Name
                    : "Sản phẩm chưa xác định",
                Color = lot.Variant != null ? lot.Variant.Color : null,
                Storage = lot.Variant != null ? lot.Variant.Storage : null,
                Ram = lot.Variant != null ? lot.Variant.Ram : null,
                ImageUrl = lot.Variant != null
                    ? lot.Variant.ImageUrl ?? lot.Variant.Product!.MainImage
                    : null
            })
            .ToListAsync(cancellationToken);

        var stockByVariant = lotRows
            .GroupBy(item => item.VariantId)
            .Select(group => new StockSummaryRow
            {
                VariantId = group.Key,
                ProductName = group.Select(item => item.ProductName).FirstOrDefault()
                    ?? "Sản phẩm chưa xác định",
                VariantLabel = BuildVariantLabel(
                    group.Select(item => item.Color).FirstOrDefault(),
                    group.Select(item => item.Storage).FirstOrDefault(),
                    group.Select(item => item.Ram).FirstOrDefault()),
                ImageUrl = group.Select(item => item.ImageUrl).FirstOrDefault(),
                OnHand = group.Sum(item => Math.Max(0, item.RemainingQuantity)),
                InventoryValue = group.Sum(item =>
                    Math.Max(0, item.RemainingQuantity) * Math.Max(0m, item.UnitCost)),
                OldestReceivedDate = group.Min(item => item.ReceivedDate)
            })
            .ToList();

        var variantIds = stockByVariant.Select(item => item.VariantId).ToList();
        var reservationByVariant = variantIds.Count == 0 || !warehouse.IsPrimary
            ? new Dictionary<int, int>()
            : await _context.Set<OrderReservations>()
                .AsNoTracking()
                .Where(item => variantIds.Contains(item.VariantId)
                    && item.Status == OrderReservationStatuses.Reserved
                    && (!item.ExpiresAt.HasValue || item.ExpiresAt > now))
                .GroupBy(item => item.VariantId)
                .Select(group => new
                {
                    VariantId = group.Key,
                    Quantity = group.Sum(item => item.Quantity)
                })
                .ToDictionaryAsync(
                    item => item.VariantId,
                    item => Math.Max(0, item.Quantity),
                    cancellationToken);

        int totalOnHand = stockByVariant.Sum(item => item.OnHand);
        int totalReserved = stockByVariant.Sum(item =>
            reservationByVariant.GetValueOrDefault(item.VariantId));
        int totalAvailable = Math.Max(0, totalOnHand - totalReserved);
        decimal totalInventoryValue = RoundMoney(
            stockByVariant.Sum(item => item.InventoryValue));
        int lowStockCount = stockByVariant.Count(item =>
            Math.Max(0, item.OnHand - reservationByVariant.GetValueOrDefault(item.VariantId))
                <= LowStockThreshold);
        int agedStockCount = stockByVariant.Count(item =>
            item.OnHand > 0 && item.OldestReceivedDate < agedThreshold);

        var purchaseRows = await _context.PurchaseOrders
            .AsNoTracking()
            .Where(order => order.WarehouseId == warehouse.WarehouseId
                && order.OrderDate.HasValue
                && order.OrderDate.Value >= fromInclusive
                && order.OrderDate.Value < toExclusive)
            .Select(order => new
            {
                order.TotalAmount,
                order.GoodsSubtotal,
                order.InputVatAmount,
                order.InboundShippingFee,
                order.OtherCost,
                order.InventoryCapitalizedCost
            })
            .ToListAsync(cancellationToken);

        decimal inboundSpend = RoundMoney(purchaseRows.Sum(order =>
            order.TotalAmount.GetValueOrDefault() > 0m
                ? order.TotalAmount.GetValueOrDefault()
                : order.GoodsSubtotal
                    + order.InputVatAmount
                    + order.InboundShippingFee
                    + order.OtherCost));
        decimal inboundInventoryValue = RoundMoney(purchaseRows.Sum(order =>
            order.InventoryCapitalizedCost));

        OrderProfitOverview onlineFinance = await OrderProfitService.GetOverviewAsync(
            _context,
            fromInclusive,
            toExclusive,
            cancellationToken);

        var distributionRows = await _context.SalesOrders
            .AsNoTracking()
            .Where(order => order.OrderDate >= fromInclusive
                && order.OrderDate < toExclusive
                && order.Status == "Completed")
            .Select(order => new
            {
                order.TotalAmount,
                order.COGSTotal,
                order.ProfitTotal
            })
            .ToListAsync(cancellationToken);

        decimal distributionRevenue = RoundMoney(
            distributionRows.Sum(order => Math.Max(0m, order.TotalAmount)));
        decimal distributionCogs = RoundMoney(
            distributionRows.Sum(order => Math.Max(0m, order.COGSTotal)));
        decimal distributionProfit = RoundMoney(
            distributionRows.Sum(order => order.ProfitTotal));
        decimal soldStockCost = RoundMoney(onlineFinance.ActiveCogs + distributionCogs);
        decimal tentativeProfit = RoundMoney(
            onlineFinance.CashContributionProfit + distributionProfit);

        var movementRows = await _context.InventoryTransactions
            .AsNoTracking()
            .Where(item => item.WarehouseId == warehouse.WarehouseId
                && item.TransactionDate.HasValue
                && item.TransactionDate.Value >= fromInclusive
                && item.TransactionDate.Value < toExclusive)
            .Select(item => new
            {
                item.TransactionDate,
                item.Quantity
            })
            .ToListAsync(cancellationToken);

        var movementByDate = movementRows
            .GroupBy(item => item.TransactionDate!.Value.Date)
            .ToDictionary(
                group => group.Key,
                group => new DailyMovement
                {
                    Inbound = group.Where(item => item.Quantity > 0)
                        .Sum(item => item.Quantity),
                    Outbound = Math.Abs(group.Where(item => item.Quantity < 0)
                        .Sum(item => item.Quantity))
                });

        int dayCount = (toInclusive - fromInclusive).Days + 1;
        var movement = Enumerable.Range(0, dayCount)
            .Select(offset => fromInclusive.AddDays(offset))
            .Select(date =>
            {
                movementByDate.TryGetValue(date, out DailyMovement? daily);
                return new InventoryMovementPoint
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    Label = date.ToString("dd/MM"),
                    Inbound = daily?.Inbound ?? 0,
                    Outbound = daily?.Outbound ?? 0
                };
            })
            .ToList();

        var warehouseValues = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot => lot.IsActive
                && !lot.IsDeleted
                && lot.RemainingQuantity > 0
                && lot.Warehouse != null
                && lot.Warehouse.IsActive)
            .GroupBy(lot => new
            {
                lot.WarehouseId,
                lot.Warehouse!.WarehouseName
            })
            .Select(group => new InventoryWarehouseValueRow
            {
                WarehouseId = group.Key.WarehouseId,
                WarehouseName = group.Key.WarehouseName,
                Quantity = group.Sum(item => item.RemainingQuantity),
                InventoryValue = group.Sum(item => item.RemainingQuantity * item.UnitCost)
            })
            .OrderByDescending(item => item.InventoryValue)
            .ToListAsync(cancellationToken);

        var onlineProductRows = await _context.OrderDetails
            .AsNoTracking()
            .Where(detail => detail.VariantId.HasValue
                && detail.Order != null
                && detail.Order.OrderDate.HasValue
                && detail.Order.OrderDate.Value >= fromInclusive
                && detail.Order.OrderDate.Value < toExclusive
                && (detail.Order.Status == "Delivered"
                    || detail.Order.Status == "Completed"))
            .GroupBy(detail => detail.VariantId!.Value)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(item => item.Quantity ?? 0),
                Revenue = group.Sum(item => item.NetRevenueAmount > 0m
                    ? item.NetRevenueAmount
                    : item.LineTotal ?? ((item.Quantity ?? 0) * (item.UnitPrice ?? 0m))),
                Profit = group.Sum(item => item.GrossProfitAmount)
            })
            .ToListAsync(cancellationToken);

        var distributionProductRows = await _context.SalesOrderDetails
            .AsNoTracking()
            .Where(detail => detail.SalesOrder != null
                && detail.SalesOrder.OrderDate >= fromInclusive
                && detail.SalesOrder.OrderDate < toExclusive
                && detail.SalesOrder.Status == "Completed")
            .GroupBy(detail => detail.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(item => item.Quantity),
                Revenue = group.Sum(item => item.TotalAmount),
                Profit = group.Sum(item => item.Profit)
            })
            .ToListAsync(cancellationToken);

        var productStats = onlineProductRows.ToDictionary(
            item => item.VariantId,
            item => new ProductPerformanceAccumulator
            {
                VariantId = item.VariantId,
                Quantity = item.Quantity,
                Revenue = item.Revenue,
                Profit = item.Profit
            });
        foreach (var row in distributionProductRows)
        {
            if (!productStats.TryGetValue(row.VariantId, out ProductPerformanceAccumulator? current))
            {
                current = new ProductPerformanceAccumulator { VariantId = row.VariantId };
                productStats[row.VariantId] = current;
            }
            current.Quantity += row.Quantity;
            current.Revenue += row.Revenue;
            current.Profit += row.Profit;
        }

        var performanceVariantIds = productStats.Keys.ToList();
        var productNames = new Dictionary<int, ProductDisplayInfo>();
        if (performanceVariantIds.Count > 0)
        {
            var productNameRows = await _context.ProductVariants
                .AsNoTracking()
                .Where(item => performanceVariantIds.Contains(item.VariantId))
                .Select(item => new
                {
                    item.VariantId,
                    ProductName = item.Product.Name,
                    item.Color,
                    item.Storage,
                    item.Ram,
                    ImageUrl = item.ImageUrl ?? item.Product.MainImage
                })
                .ToListAsync(cancellationToken);

            productNames = productNameRows.ToDictionary(
                item => item.VariantId,
                item => new ProductDisplayInfo
                {
                    VariantId = item.VariantId,
                    ProductName = item.ProductName,
                    Color = item.Color,
                    Storage = item.Storage,
                    Ram = item.Ram,
                    ImageUrl = item.ImageUrl
                });
        }

        var topProducts = productStats.Values
            .OrderByDescending(item => item.Quantity)
            .ThenByDescending(item => item.Profit)
            .Take(8)
            .Select(item =>
            {
                ProductDisplayInfo? product = productNames.GetValueOrDefault(item.VariantId);
                return new InventoryProductPerformanceRow
                {
                    VariantId = item.VariantId,
                    Sku = $"SKU-{item.VariantId:D6}",
                    ProductName = product?.ProductName ?? $"Biến thể #{item.VariantId}",
                    VariantLabel = product == null
                        ? "Biến thể"
                        : BuildVariantLabel(product.Color, product.Storage, product.Ram),
                    ImageUrl = product?.ImageUrl ?? "/images/products/default-product.png",
                    Quantity = item.Quantity,
                    Revenue = RoundMoney(item.Revenue),
                    Profit = RoundMoney(item.Profit)
                };
            })
            .ToList();

        var lowStock = stockByVariant
            .Select(item => new InventoryLowStockRow
            {
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                VariantLabel = item.VariantLabel,
                ImageUrl = string.IsNullOrWhiteSpace(item.ImageUrl)
                    ? "/images/products/default-product.png"
                    : item.ImageUrl,
                Available = Math.Max(0,
                    item.OnHand - reservationByVariant.GetValueOrDefault(item.VariantId)),
                OnHand = item.OnHand
            })
            .Where(item => item.Available <= LowStockThreshold)
            .OrderBy(item => item.Available)
            .ThenBy(item => item.ProductName)
            .Take(8)
            .ToList();

        var agedStock = stockByVariant
            .Where(item => item.OnHand > 0 && item.OldestReceivedDate < agedThreshold)
            .OrderBy(item => item.OldestReceivedDate)
            .Take(8)
            .Select(item => new InventoryAgedStockRow
            {
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                VariantLabel = item.VariantLabel,
                Quantity = item.OnHand,
                InventoryValue = RoundMoney(item.InventoryValue),
                AgeDays = Math.Max(0, (now.Date - item.OldestReceivedDate.Date).Days)
            })
            .ToList();

        var recentActivity = await _context.InventoryTransactions
            .AsNoTracking()
            .Where(item => item.WarehouseId == warehouse.WarehouseId)
            .OrderByDescending(item => item.TransactionDate)
            .ThenByDescending(item => item.TransactionId)
            .Take(10)
            .Select(item => new InventoryRecentActivityRow
            {
                TransactionId = item.TransactionId,
                TransactionType = item.TransactionType,
                Quantity = item.Quantity,
                TransactionDate = item.TransactionDate,
                ProductName = item.Variant != null && item.Variant.Product != null
                    ? item.Variant.Product.Name
                    : $"Biến thể #{item.VariantId}",
                Sku = $"SKU-{item.VariantId:D6}",
                Note = item.Note
            })
            .ToListAsync(cancellationToken);

        return new InventoryDashboardResponse
        {
            Period = new InventoryDashboardPeriod
            {
                FromDate = fromInclusive.ToString("yyyy-MM-dd"),
                ToDate = toInclusive.ToString("yyyy-MM-dd")
            },
            Warehouse = new InventoryDashboardWarehouse
            {
                WarehouseId = warehouse.WarehouseId,
                WarehouseCode = warehouse.WarehouseCode,
                WarehouseName = warehouse.WarehouseName,
                IsPrimary = warehouse.IsPrimary
            },
            Summary = new InventoryDashboardSummary
            {
                TotalOnHand = totalOnHand,
                TotalReserved = totalReserved,
                TotalAvailable = totalAvailable,
                TotalInventoryValue = totalInventoryValue,
                InboundSpend = inboundSpend,
                InboundInventoryValue = inboundInventoryValue,
                ActualCashReceived = onlineFinance.NetCashCollected,
                OutstandingAmount = onlineFinance.OutstandingAmount,
                SoldStockCost = soldStockCost,
                TentativeProfit = tentativeProfit,
                DistributionRevenue = distributionRevenue,
                LowStockCount = lowStockCount,
                AgedStockCount = agedStockCount,
                ActiveVariantCount = stockByVariant.Count
            },
            Movement = movement,
            WarehouseValues = warehouseValues,
            TopProducts = topProducts,
            LowStock = lowStock,
            AgedStock = agedStock,
            RecentActivity = recentActivity,
            FinanceNote = "Lợi nhuận tạm tính chưa bao gồm đầy đủ phí cổng thanh toán và các khoản vận chuyển chưa được đối soát.",
            FinanceScope = "Các chỉ số tiền thu, giá vốn, lợi nhuận và sản phẩm bán trong kỳ được tính trên toàn hệ thống; lượng hàng, tiền nhập và cảnh báo được tính theo kho đang chọn."
        };
    }

    private async Task<Warehouses> ResolveWarehouseAsync(
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        Warehouses? warehouse;
        if (warehouseId.HasValue && warehouseId.Value > 0)
        {
            warehouse = await _context.Warehouses
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.WarehouseId == warehouseId.Value
                    && item.IsActive,
                    cancellationToken);
        }
        else
        {
            warehouse = await _context.Warehouses
                .AsNoTracking()
                .Where(item => item.IsActive)
                .OrderByDescending(item => item.IsPrimary)
                .ThenBy(item => item.WarehouseId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return warehouse
            ?? throw new InvalidOperationException("Chưa có kho hoạt động để quản lý tồn.");
    }

    private static string BuildVariantLabel(string? color, string? storage, string? ram)
    {
        string[] parts = new[] { color, storage, ram }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
        return parts.Length == 0 ? "Biến thể mặc định" : string.Join(" / ", parts);
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed class StockSummaryRow
    {
        public int VariantId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public string VariantLabel { get; init; } = string.Empty;
        public string? ImageUrl { get; init; }
        public int OnHand { get; init; }
        public decimal InventoryValue { get; init; }
        public DateTime OldestReceivedDate { get; init; }
    }

    private sealed class DailyMovement
    {
        public int Inbound { get; init; }
        public int Outbound { get; init; }
    }

    private sealed class ProductDisplayInfo
    {
        public int VariantId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public string? Color { get; init; }
        public string? Storage { get; init; }
        public string? Ram { get; init; }
        public string? ImageUrl { get; init; }
    }

    private sealed class ProductPerformanceAccumulator
    {
        public int VariantId { get; init; }
        public int Quantity { get; set; }
        public decimal Revenue { get; set; }
        public decimal Profit { get; set; }
    }
}
