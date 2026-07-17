using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

/// <summary>
/// Cổng duy nhất thay đổi tồn kho theo vòng đời đơn bán lẻ.
/// Phase B tiêu thụ tồn theo FIFO của một kho thực, lưu snapshot giá vốn
/// theo từng OrderDetail/Lot và hoàn lại đúng lô khi hủy hoặc trả hàng.
/// Transaction và SaveChanges vẫn do application flow bên ngoài quản lý.
/// </summary>
public sealed class OrderInventoryService : IOrderInventoryService
{
    private readonly ApplicationDbContext _context;

    public OrderInventoryService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> DeductOrderStockAsync(
        int orderId,
        string reason,
        DateTime? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();

        var order = await LoadOrderAsync(orderId, cancellationToken);
        var timestamp = occurredAt ?? DateTime.Now;

        var claimed = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
              UPDATE Orders
              SET IsStockDeducted = 1,
                  StockDeductedAt = {timestamp}
              WHERE OrderId = {orderId}
                AND IsStockDeducted = 0
              """,
            cancellationToken);

        if (claimed == 0)
        {
            return false;
        }

        var details = order.OrderDetails
            .Where(detail =>
                detail.VariantId.HasValue
                && (detail.Quantity ?? 0) > 0
                && detail.Variant != null)
            .OrderBy(detail => detail.OrderDetailId)
            .ToList();

        if (details.Count == 0)
        {
            throw new InvalidOperationException(
                $"Đơn hàng #{orderId} không có dòng sản phẩm hợp lệ để trừ kho.");
        }

        int warehouseId = await ResolveFulfillmentWarehouseIdAsync(
            order,
            details,
            cancellationToken);
        order.FulfillmentWarehouseId = warehouseId;

        var existingAllocations = await _context.OrderInventoryAllocations
            .Where(item =>
                item.OrderId == orderId
                && item.Status == OrderInventoryAllocationStatuses.Consumed)
            .AnyAsync(cancellationToken);
        if (existingAllocations)
        {
            throw new InvalidOperationException(
                $"Đơn hàng #{orderId} đã có allocation FIFO đang hoạt động nhưng cờ tồn kho chưa đồng bộ.");
        }

        var lineFinancials = CalculateOrderLineNetRevenue(order, details);
        var touchedVariantIds = new HashSet<int>();

        foreach (var detail in details)
        {
            int variantId = detail.VariantId!.Value;
            int requiredQuantity = detail.Quantity!.Value;
            int remainingQuantity = requiredQuantity;
            decimal detailCogs = 0m;

            var lots = await _context.InventoryLots
                .Where(lot =>
                    lot.WarehouseId == warehouseId
                    && lot.VariantId == variantId
                    && lot.RemainingQuantity > 0
                    && lot.IsActive
                    && !lot.IsDeleted)
                .OrderBy(lot => lot.ReceivedDate)
                .ThenBy(lot => lot.LotId)
                .ToListAsync(cancellationToken);

            int available = lots.Sum(lot => lot.RemainingQuantity);
            if (available < requiredQuantity)
            {
                throw new InvalidOperationException(
                    $"Kho #{warehouseId} chỉ còn {available} sản phẩm của biến thể #{variantId}, cần {requiredQuantity}.");
            }

            foreach (var lot in lots)
            {
                if (remainingQuantity <= 0)
                {
                    break;
                }

                int takeQuantity = Math.Min(
                    lot.RemainingQuantity,
                    remainingQuantity);

                int affected = await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                      UPDATE InventoryLots
                      SET RemainingQuantity = RemainingQuantity - {takeQuantity}
                      WHERE LotId = {lot.LotId}
                        AND WarehouseId = {warehouseId}
                        AND RemainingQuantity >= {takeQuantity}
                        AND IsActive = 1
                        AND IsDeleted = 0
                      """,
                    cancellationToken);

                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        $"Lô #{lot.LotId} vừa bị thay đổi bởi giao dịch khác. Vui lòng thử lại.");
                }

                lot.RemainingQuantity -= takeQuantity;
                decimal totalCost = RoundMoney(takeQuantity * lot.UnitCost);
                detailCogs += totalCost;
                remainingQuantity -= takeQuantity;

                var serials = await _context.ProductSerials
                    .Where(serial =>
                        serial.VariantId == variantId
                        && serial.LotId == lot.LotId
                        && serial.Status == "InStock")
                    .OrderBy(serial => serial.CreatedDate)
                    .ThenBy(serial => serial.SerialId)
                    .Take(takeQuantity)
                    .ToListAsync(cancellationToken);

                if (serials.Count != takeQuantity)
                {
                    throw new InvalidOperationException(
                        $"Lô #{lot.LotId} có {serials.Count} serial InStock nhưng cần {takeQuantity}. Hãy đối soát tồn lô và serial.");
                }

                foreach (var serial in serials)
                {
                    serial.Status = "Sold";
                    serial.OrderId = order.OrderId;
                    serial.SoldDate = timestamp;
                }

                _context.OrderInventoryAllocations.Add(
                    new OrderInventoryAllocations
                    {
                        OrderId = order.OrderId,
                        OrderDetailId = detail.OrderDetailId,
                        VariantId = variantId,
                        LotId = lot.LotId,
                        WarehouseId = warehouseId,
                        Quantity = takeQuantity,
                        UnitCost = lot.UnitCost,
                        TotalCost = totalCost,
                        Status = OrderInventoryAllocationStatuses.Consumed,
                        AllocatedAt = timestamp,
                        Reason = reason
                    });

                _context.InventoryTransactions.Add(
                    new InventoryTransactions
                    {
                        VariantId = variantId,
                        TransactionType = "OUT_ORDER_FIFO",
                        Quantity = -takeQuantity,
                        ReferenceId = order.OrderId,
                        TransactionDate = timestamp,
                        WarehouseId = warehouseId,
                        LotId = lot.LotId,
                        UnitCostSnapshot = lot.UnitCost,
                        TotalCostSnapshot = totalCost,
                        Note =
                            $"{reason}. Đơn #{order.OrderId}; chi tiết #{detail.OrderDetailId}; "
                            + $"xuất FIFO lô #{lot.LotId}, {takeQuantity} sản phẩm."
                    });
            }

            if (remainingQuantity != 0)
            {
                throw new InvalidOperationException(
                    $"Không hoàn tất được allocation FIFO cho chi tiết #{detail.OrderDetailId}.");
            }

            detail.CogsAmount = RoundMoney(detailCogs);
            detail.NetRevenueAmount = lineFinancials[detail.OrderDetailId];
            detail.GrossProfitAmount = RoundMoney(
                detail.NetRevenueAmount - detail.CogsAmount);
            detail.CostCalculatedAt = timestamp;
            touchedVariantIds.Add(variantId);
        }

        order.MerchandiseNetRevenueAmount = RoundMoney(
            details.Sum(detail => detail.NetRevenueAmount));
        order.CogsAmount = RoundMoney(
            details.Sum(detail => detail.CogsAmount));
        order.GrossProfitAmount = RoundMoney(
            order.MerchandiseNetRevenueAmount - order.CogsAmount);
        order.CostCalculatedAt = timestamp;
        order.IsStockDeducted = true;
        order.StockDeductedAt = timestamp;

        await SynchronizeVariantStocksAsync(
            touchedVariantIds,
            cancellationToken);
        await UpdateReservationsAsync(
            order,
            OrderReservationStatuses.Consumed,
            reason,
            timestamp,
            cancellationToken);

        return true;
    }

    public async Task<bool> RestoreOrderStockAsync(
        int orderId,
        string reason,
        bool restoreFlashSaleSlots = true,
        DateTime? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();

        var order = await LoadOrderAsync(orderId, cancellationToken);
        var timestamp = occurredAt ?? DateTime.Now;

        var claimed = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
              UPDATE Orders
              SET IsStockDeducted = 0,
                  StockDeductedAt = NULL
              WHERE OrderId = {orderId}
                AND IsStockDeducted = 1
              """,
            cancellationToken);

        if (claimed == 0)
        {
            return false;
        }

        var allocations = await _context.OrderInventoryAllocations
            .Include(item => item.Lot)
            .Where(item =>
                item.OrderId == orderId
                && item.Status == OrderInventoryAllocationStatuses.Consumed)
            .OrderBy(item => item.AllocationId)
            .ToListAsync(cancellationToken);

        var touchedVariantIds = new HashSet<int>();

        if (allocations.Count == 0)
        {
            await RestoreLegacyOrderIntoAdjustmentLotsAsync(
                order,
                reason,
                timestamp,
                touchedVariantIds,
                cancellationToken);
        }
        else
        {
            foreach (var allocation in allocations)
            {
                var targetLot = allocation.Lot;
                if (targetLot == null
                    || targetLot.IsDeleted
                    || !targetLot.IsActive)
                {
                    targetLot = new InventoryLots
                    {
                        Poid = 0,
                        VariantId = allocation.VariantId,
                        SupplierId = allocation.Lot?.SupplierId ?? 1,
                        WarehouseId = allocation.WarehouseId,
                        ReceivedQuantity = allocation.Quantity,
                        RemainingQuantity = allocation.Quantity,
                        UnitCost = allocation.UnitCost,
                        ReceivedDate = timestamp,
                        IsActive = true,
                        IsDeleted = false
                    };
                    _context.InventoryLots.Add(targetLot);
                    allocation.RestoredLotId = null;
                }
                else
                {
                    targetLot.RemainingQuantity = checked(
                        targetLot.RemainingQuantity + allocation.Quantity);
                    allocation.RestoredLotId = targetLot.LotId;
                }

                var serials = await _context.ProductSerials
                    .Where(serial =>
                        serial.OrderId == orderId
                        && serial.VariantId == allocation.VariantId
                        && serial.LotId == allocation.LotId
                        && serial.Status == "Sold")
                    .OrderBy(serial => serial.SerialId)
                    .Take(allocation.Quantity)
                    .ToListAsync(cancellationToken);

                if (serials.Count != allocation.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Allocation #{allocation.AllocationId} không đủ serial Sold để hoàn đúng lô.");
                }

                foreach (var serial in serials)
                {
                    serial.Status = "InStock";
                    serial.OrderId = null;
                    serial.SoldDate = null;
                    serial.Lot = targetLot;
                }

                allocation.Status = OrderInventoryAllocationStatuses.Restored;
                allocation.RestoredAt = timestamp;
                allocation.Reason = reason;
                touchedVariantIds.Add(allocation.VariantId);

                _context.InventoryTransactions.Add(
                    new InventoryTransactions
                    {
                        VariantId = allocation.VariantId,
                        TransactionType = "IN_ORDER_RETURN",
                        Quantity = allocation.Quantity,
                        ReferenceId = orderId,
                        TransactionDate = timestamp,
                        WarehouseId = allocation.WarehouseId,
                        Lot = targetLot,
                        UnitCostSnapshot = allocation.UnitCost,
                        TotalCostSnapshot = allocation.TotalCost,
                        Note =
                            $"{reason}. Hoàn đơn #{orderId}; allocation #{allocation.AllocationId}; "
                            + (targetLot.LotId > 0
                                ? $"nhập lại lô #{targetLot.LotId}."
                                : "nhập vào lô hoàn hàng mới.")
                    });
            }
        }

        await SynchronizeVariantStocksAsync(
            touchedVariantIds,
            cancellationToken);
        await UpdateReservationsAsync(
            order,
            OrderReservationStatuses.Released,
            reason,
            timestamp,
            cancellationToken);

        if (restoreFlashSaleSlots)
        {
            RestoreFlashSaleSlots(order);
        }

        order.IsStockDeducted = false;
        order.StockDeductedAt = null;
        return true;
    }

    public async Task<bool> CloseOrderStockWithoutRestockAsync(
        int orderId,
        string reason,
        DateTime? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();

        var order = await LoadOrderAsync(orderId, cancellationToken);
        var timestamp = occurredAt ?? DateTime.Now;

        var claimed = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
              UPDATE Orders
              SET IsStockDeducted = 0,
                  StockDeductedAt = NULL
              WHERE OrderId = {orderId}
                AND IsStockDeducted = 1
              """,
            cancellationToken);

        if (claimed == 0)
        {
            return false;
        }

        var allocations = await _context.OrderInventoryAllocations
            .Where(item =>
                item.OrderId == orderId
                && item.Status == OrderInventoryAllocationStatuses.Consumed)
            .ToListAsync(cancellationToken);

        foreach (var allocation in allocations)
        {
            allocation.Status = OrderInventoryAllocationStatuses.Damaged;
            allocation.ClosedAt = timestamp;
            allocation.Reason = reason;

            var serials = await _context.ProductSerials
                .Where(serial =>
                    serial.OrderId == orderId
                    && serial.VariantId == allocation.VariantId
                    && serial.LotId == allocation.LotId
                    && serial.Status == "Sold")
                .Take(allocation.Quantity)
                .ToListAsync(cancellationToken);

            foreach (var serial in serials)
            {
                serial.Status = "Damaged";
            }

            _context.InventoryTransactions.Add(
                new InventoryTransactions
                {
                    VariantId = allocation.VariantId,
                    TransactionType = "RETURN_DAMAGED",
                    Quantity = 0,
                    ReferenceId = orderId,
                    TransactionDate = timestamp,
                    WarehouseId = allocation.WarehouseId,
                    LotId = allocation.LotId,
                    UnitCostSnapshot = allocation.UnitCost,
                    TotalCostSnapshot = allocation.TotalCost,
                    Note =
                        $"{reason}. Đơn #{orderId}; allocation #{allocation.AllocationId}; "
                        + $"{allocation.Quantity} sản phẩm không nhập lại tồn bán."
                });
        }

        await UpdateReservationsAsync(
            order,
            OrderReservationStatuses.Damaged,
            reason,
            timestamp,
            cancellationToken);

        order.IsStockDeducted = false;
        order.StockDeductedAt = null;
        return true;
    }

    private async Task<int> ResolveFulfillmentWarehouseIdAsync(
        Orders order,
        IReadOnlyCollection<OrderDetails> details,
        CancellationToken cancellationToken)
    {
        var requiredByVariant = details
            .GroupBy(detail => detail.VariantId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(detail => detail.Quantity ?? 0));

        var warehouses = await _context.Warehouses
            .Where(warehouse => warehouse.IsActive)
            .OrderByDescending(warehouse => warehouse.IsPrimary)
            .ThenBy(warehouse => warehouse.WarehouseId)
            .ToListAsync(cancellationToken);

        if (warehouses.Count == 0)
        {
            throw new InvalidOperationException(
                "Hệ thống chưa có kho hoạt động để xử lý đơn hàng.");
        }

        if (order.FulfillmentWarehouseId.HasValue)
        {
            var configured = warehouses.FirstOrDefault(warehouse =>
                warehouse.WarehouseId == order.FulfillmentWarehouseId.Value);
            if (configured == null)
            {
                throw new InvalidOperationException(
                    $"Kho xử lý #{order.FulfillmentWarehouseId} của đơn hàng không còn hoạt động.");
            }

            if (await WarehouseCanFulfillAsync(
                    configured.WarehouseId,
                    requiredByVariant,
                    cancellationToken))
            {
                return configured.WarehouseId;
            }

            throw new InvalidOperationException(
                $"Kho {configured.WarehouseName} không đủ toàn bộ sản phẩm của đơn #{order.OrderId}.");
        }

        foreach (var warehouse in warehouses)
        {
            if (await WarehouseCanFulfillAsync(
                    warehouse.WarehouseId,
                    requiredByVariant,
                    cancellationToken))
            {
                return warehouse.WarehouseId;
            }
        }

        throw new InvalidOperationException(
            "Không có một kho duy nhất đủ toàn bộ sản phẩm. Hệ thống chưa cho phép tách một đơn bán lẻ qua nhiều kho.");
    }

    private async Task<bool> WarehouseCanFulfillAsync(
        int warehouseId,
        IReadOnlyDictionary<int, int> requiredByVariant,
        CancellationToken cancellationToken)
    {
        var variantIds = requiredByVariant.Keys.ToList();
        var available = await _context.InventoryLots
            .Where(lot =>
                lot.WarehouseId == warehouseId
                && variantIds.Contains(lot.VariantId)
                && lot.RemainingQuantity > 0
                && lot.IsActive
                && !lot.IsDeleted)
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

        return requiredByVariant.All(required =>
            available.GetValueOrDefault(required.Key) >= required.Value);
    }

    private async Task SynchronizeVariantStocksAsync(
        IEnumerable<int> variantIds,
        CancellationToken cancellationToken)
    {
        foreach (int variantId in variantIds.Distinct())
        {
            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(
                    item => item.VariantId == variantId,
                    cancellationToken);
            if (variant == null)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy biến thể #{variantId} để đồng bộ tồn tổng.");
            }

            var persistedLots = await _context.InventoryLots
                .Where(lot =>
                    lot.VariantId == variantId
                    && lot.IsActive
                    && !lot.IsDeleted)
                .ToListAsync(cancellationToken);

            int addedStock = _context.ChangeTracker
                .Entries<InventoryLots>()
                .Where(entry =>
                    entry.State == EntityState.Added
                    && entry.Entity.VariantId == variantId
                    && entry.Entity.IsActive
                    && !entry.Entity.IsDeleted)
                .Sum(entry => Math.Max(0, entry.Entity.RemainingQuantity));

            variant.Stock = persistedLots.Sum(lot =>
                    Math.Max(0, lot.RemainingQuantity))
                + addedStock;
        }
    }

    private async Task RestoreLegacyOrderIntoAdjustmentLotsAsync(
        Orders order,
        string reason,
        DateTime timestamp,
        ISet<int> touchedVariantIds,
        CancellationToken cancellationToken)
    {
        int warehouseId = order.FulfillmentWarehouseId
            ?? await _context.Warehouses
                .Where(warehouse => warehouse.IsActive)
                .OrderByDescending(warehouse => warehouse.IsPrimary)
                .ThenBy(warehouse => warehouse.WarehouseId)
                .Select(warehouse => warehouse.WarehouseId)
                .FirstAsync(cancellationToken);

        foreach (var detail in order.OrderDetails.Where(detail =>
                     detail.VariantId.HasValue
                     && (detail.Quantity ?? 0) > 0))
        {
            int variantId = detail.VariantId!.Value;
            int quantity = detail.Quantity!.Value;
            decimal unitCost = detail.CogsAmount > 0m
                ? RoundMoney(detail.CogsAmount / quantity)
                : detail.Variant?.CostPrice ?? 0m;

            var returnLot = new InventoryLots
            {
                Poid = 0,
                VariantId = variantId,
                SupplierId = 1,
                WarehouseId = warehouseId,
                ReceivedQuantity = quantity,
                RemainingQuantity = quantity,
                UnitCost = unitCost,
                ReceivedDate = timestamp,
                IsActive = true,
                IsDeleted = false
            };
            _context.InventoryLots.Add(returnLot);

            for (int index = 1; index <= quantity; index++)
            {
                _context.ProductSerials.Add(new ProductSerials
                {
                    VariantId = variantId,
                    Lot = returnLot,
                    SerialNumber =
                        $"LEGACY-RET-{order.OrderId}-{detail.OrderDetailId}-{Guid.NewGuid():N}",
                    Status = "InStock",
                    CreatedDate = timestamp
                });
            }

            _context.InventoryTransactions.Add(
                new InventoryTransactions
                {
                    VariantId = variantId,
                    TransactionType = "IN_LEGACY_RETURN",
                    Quantity = quantity,
                    ReferenceId = order.OrderId,
                    TransactionDate = timestamp,
                    WarehouseId = warehouseId,
                    Lot = returnLot,
                    UnitCostSnapshot = unitCost,
                    TotalCostSnapshot = RoundMoney(unitCost * quantity),
                    Note =
                        $"{reason}. Đơn cũ #{order.OrderId} không có allocation FIFO; "
                        + "tạo lô hoàn hàng đối soát mới."
                });

            touchedVariantIds.Add(variantId);
        }
    }

    private async Task UpdateReservationsAsync(
        Orders order,
        string status,
        string reason,
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        var quantities = order.OrderDetails
            .Where(detail =>
                detail.VariantId.HasValue
                && (detail.Quantity ?? 0) > 0)
            .GroupBy(detail => detail.VariantId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(detail => detail.Quantity ?? 0));

        var reservations = await _context.OrderReservations
            .Where(item => item.OrderId == order.OrderId)
            .ToDictionaryAsync(
                item => item.VariantId,
                cancellationToken);

        foreach (var item in quantities)
        {
            if (!reservations.TryGetValue(item.Key, out var reservation))
            {
                reservation = new OrderReservations
                {
                    OrderId = order.OrderId,
                    VariantId = item.Key,
                    ReservedAt = order.StockDeductedAt ?? timestamp
                };
                _context.OrderReservations.Add(reservation);
                reservations[item.Key] = reservation;
            }

            reservation.Quantity = item.Value;
            reservation.Status = status;
            reservation.Reason = reason;
            reservation.ExpiresAt = null;

            if (status == OrderReservationStatuses.Consumed)
            {
                reservation.ConsumedAt = timestamp;
                reservation.ReleasedAt = null;
            }
            else
            {
                reservation.ReleasedAt = timestamp;
            }
        }
    }

    private static Dictionary<int, decimal> CalculateOrderLineNetRevenue(
        Orders order,
        IReadOnlyList<OrderDetails> details)
    {
        var grossLines = details.ToDictionary(
            detail => detail.OrderDetailId,
            detail => RoundMoney(
                detail.LineTotal
                ?? ((detail.UnitPrice ?? 0m) * (detail.Quantity ?? 0))));

        decimal totalGross = grossLines.Values.Sum();
        decimal orderDiscount = Math.Clamp(
            order.DiscountAmount,
            0m,
            Math.Max(0m, totalGross));
        decimal allocatedDiscount = 0m;
        var result = new Dictionary<int, decimal>();

        for (int index = 0; index < details.Count; index++)
        {
            var detail = details[index];
            decimal grossLine = grossLines[detail.OrderDetailId];
            decimal lineDiscount;

            if (index == details.Count - 1)
            {
                lineDiscount = RoundMoney(orderDiscount - allocatedDiscount);
            }
            else if (totalGross <= 0m)
            {
                lineDiscount = 0m;
            }
            else
            {
                lineDiscount = RoundMoney(
                    orderDiscount * grossLine / totalGross);
                allocatedDiscount += lineDiscount;
            }

            result[detail.OrderDetailId] = RoundMoney(
                Math.Max(0m, grossLine - lineDiscount));
        }

        return result;
    }

    private static void RestoreFlashSaleSlots(Orders order)
    {
        foreach (var detail in order.OrderDetails.Where(detail =>
                     detail.IsFlashSaleItem
                     && detail.FlashSaleItemId.HasValue
                     && (detail.Quantity ?? 0) > 0))
        {
            if (detail.FlashSaleItem == null)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy suất Flash Sale #{detail.FlashSaleItemId} để hoàn cho đơn #{order.OrderId}.");
            }

            detail.FlashSaleItem.Sold = Math.Max(
                0,
                detail.FlashSaleItem.Sold - (detail.Quantity ?? 0));
        }
    }

    private async Task<Orders> LoadOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(current => current.OrderDetails)
                .ThenInclude(detail => detail.Variant)
            .Include(current => current.OrderDetails)
                .ThenInclude(detail => detail.FlashSaleItem)
            .FirstOrDefaultAsync(
                current => current.OrderId == orderId,
                cancellationToken);

        return order ?? throw new InvalidOperationException(
            $"Không tìm thấy đơn hàng #{orderId}.");
    }

    private void EnsureTransaction()
    {
        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Thay đổi tồn kho theo đơn hàng phải chạy bên trong database transaction.");
        }
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
