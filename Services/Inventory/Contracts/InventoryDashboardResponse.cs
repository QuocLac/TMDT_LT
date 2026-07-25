using System;
using System.Collections.Generic;

namespace TMDT_LT.Services.Inventory.Contracts;

public sealed class InventoryDashboardResponse
{
    public bool Success { get; init; } = true;
    public InventoryDashboardPeriod Period { get; init; } = new();
    public InventoryDashboardWarehouse Warehouse { get; init; } = new();
    public InventoryDashboardSummary Summary { get; init; } = new();
    public IReadOnlyList<InventoryMovementPoint> Movement { get; init; }
        = Array.Empty<InventoryMovementPoint>();
    public IReadOnlyList<InventoryWarehouseValueRow> WarehouseValues { get; init; }
        = Array.Empty<InventoryWarehouseValueRow>();
    public IReadOnlyList<InventoryProductPerformanceRow> TopProducts { get; init; }
        = Array.Empty<InventoryProductPerformanceRow>();
    public IReadOnlyList<InventoryLowStockRow> LowStock { get; init; }
        = Array.Empty<InventoryLowStockRow>();
    public IReadOnlyList<InventoryAgedStockRow> AgedStock { get; init; }
        = Array.Empty<InventoryAgedStockRow>();
    public IReadOnlyList<InventoryRecentActivityRow> RecentActivity { get; init; }
        = Array.Empty<InventoryRecentActivityRow>();
    public string FinanceNote { get; init; } = string.Empty;
    public string FinanceScope { get; init; } = string.Empty;
}

public sealed class InventoryDashboardPeriod
{
    public string FromDate { get; init; } = string.Empty;
    public string ToDate { get; init; } = string.Empty;
}

public sealed class InventoryDashboardWarehouse
{
    public int WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
}

public sealed class InventoryDashboardSummary
{
    public int TotalOnHand { get; init; }
    public int TotalReserved { get; init; }
    public int TotalAvailable { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public decimal InboundSpend { get; init; }
    public decimal InboundInventoryValue { get; init; }
    public decimal ActualCashReceived { get; init; }
    public decimal OutstandingAmount { get; init; }
    public decimal SoldStockCost { get; init; }
    public decimal TentativeProfit { get; init; }
    public decimal DistributionRevenue { get; init; }
    public int LowStockCount { get; init; }
    public int AgedStockCount { get; init; }
    public int ActiveVariantCount { get; init; }
}

public sealed class InventoryMovementPoint
{
    public string Date { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Inbound { get; init; }
    public int Outbound { get; init; }
}

public sealed class InventoryWarehouseValueRow
{
    public int WarehouseId { get; init; }
    public string WarehouseName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal InventoryValue { get; init; }
}

public sealed class InventoryProductPerformanceRow
{
    public int VariantId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Revenue { get; init; }
    public decimal Profit { get; init; }
}

public sealed class InventoryLowStockRow
{
    public int VariantId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public int Available { get; init; }
    public int OnHand { get; init; }
}

public sealed class InventoryAgedStockRow
{
    public int VariantId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string VariantLabel { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal InventoryValue { get; init; }
    public int AgeDays { get; init; }
}

public sealed class InventoryRecentActivityRow
{
    public int TransactionId { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public DateTime? TransactionDate { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? Note { get; init; }
}
