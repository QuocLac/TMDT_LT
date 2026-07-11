using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

/// <summary>
/// Tập trung hóa thay đổi tồn kho phát sinh từ vòng đời đơn hàng.
/// Cờ Orders.IsStockDeducted được claim bằng UPDATE có điều kiện để chống
/// hai request đồng thời cùng trừ hoặc cùng hoàn tồn kho.
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

        // Claim nguyên tử: chỉ một transaction được đổi false -> true.
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

        var quantitiesByVariant = order.OrderDetails
            .Where(detail => detail.VariantId.HasValue && (detail.Quantity ?? 0) > 0)
            .GroupBy(detail => detail.VariantId!.Value)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(detail => detail.Quantity ?? 0),
                Variant = group.Select(detail => detail.Variant)
                    .FirstOrDefault(variant => variant != null)
            })
            .ToList();

        if (quantitiesByVariant.Count == 0)
        {
            throw new InvalidOperationException(
                $"Đơn hàng #{orderId} không có dòng sản phẩm hợp lệ để trừ kho.");
        }

        var reservationsByVariant = await _context.OrderReservations
            .Where(reservation => reservation.OrderId == orderId)
            .ToDictionaryAsync(reservation => reservation.VariantId, cancellationToken);

        foreach (var item in quantitiesByVariant)
        {
            if (item.Variant == null)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy biến thể #{item.VariantId} của đơn hàng #{orderId}.");
            }

            var currentStock = item.Variant.Stock ?? 0;
            if (currentStock < item.Quantity)
            {
                throw new InvalidOperationException(
                    $"Biến thể #{item.VariantId} chỉ còn {currentStock}, không đủ trừ {item.Quantity} sản phẩm.");
            }
        }

        foreach (var item in quantitiesByVariant)
        {
            var variant = item.Variant!;
            var quantityBefore = variant.Stock ?? 0;
            var quantityAfter = quantityBefore - item.Quantity;

            variant.Stock = quantityAfter;

            _context.InventoryTransactions.Add(new InventoryTransactions
            {
                VariantId = item.VariantId,
                TransactionType = "ADJUST",
                Quantity = -item.Quantity,
                ReferenceId = order.OrderId,
                TransactionDate = timestamp,
                Note = $"{reason}. Đơn #{order.OrderId}; tồn {quantityBefore} -> {quantityAfter}."
            });

            if (!reservationsByVariant.TryGetValue(item.VariantId, out var reservation))
            {
                reservation = new OrderReservations
                {
                    OrderId = order.OrderId,
                    VariantId = item.VariantId,
                    Quantity = item.Quantity,
                    ReservedAt = timestamp
                };
                _context.OrderReservations.Add(reservation);
                reservationsByVariant[item.VariantId] = reservation;
            }

            reservation.Quantity = item.Quantity;
            reservation.Status = OrderReservationStatuses.Consumed;
            reservation.ConsumedAt = timestamp;
            reservation.ReleasedAt = null;
            reservation.ExpiresAt = null;
            reservation.Reason = reason;
        }

        // Đồng bộ entity đang được tracking với UPDATE nguyên tử phía trên.
        order.IsStockDeducted = true;
        order.StockDeductedAt = timestamp;

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

        // Claim nguyên tử: chỉ một transaction được đổi true -> false.
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

        var quantitiesByVariant = order.OrderDetails
            .Where(detail => detail.VariantId.HasValue && (detail.Quantity ?? 0) > 0)
            .GroupBy(detail => detail.VariantId!.Value)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(detail => detail.Quantity ?? 0),
                Variant = group.Select(detail => detail.Variant)
                    .FirstOrDefault(variant => variant != null)
            })
            .ToList();

        var reservationsByVariant = await _context.OrderReservations
            .Where(reservation => reservation.OrderId == orderId)
            .ToDictionaryAsync(reservation => reservation.VariantId, cancellationToken);

        foreach (var item in quantitiesByVariant)
        {
            if (item.Variant == null)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy biến thể #{item.VariantId} để hoàn kho đơn #{orderId}.");
            }

            var quantityBefore = item.Variant.Stock ?? 0;
            var quantityAfter = checked(quantityBefore + item.Quantity);

            item.Variant.Stock = quantityAfter;

            _context.InventoryTransactions.Add(new InventoryTransactions
            {
                VariantId = item.VariantId,
                TransactionType = "ADJUST",
                Quantity = item.Quantity,
                ReferenceId = order.OrderId,
                TransactionDate = timestamp,
                Note = $"{reason}. Đơn #{order.OrderId}; tồn {quantityBefore} -> {quantityAfter}."
            });

            if (!reservationsByVariant.TryGetValue(item.VariantId, out var reservation))
            {
                reservation = new OrderReservations
                {
                    OrderId = order.OrderId,
                    VariantId = item.VariantId,
                    Quantity = item.Quantity,
                    ReservedAt = order.StockDeductedAt ?? timestamp
                };
                _context.OrderReservations.Add(reservation);
                reservationsByVariant[item.VariantId] = reservation;
            }

            reservation.Quantity = item.Quantity;
            reservation.Status = OrderReservationStatuses.Released;
            reservation.ReleasedAt = timestamp;
            reservation.Reason = reason;
        }

        if (restoreFlashSaleSlots)
        {
            foreach (var detail in order.OrderDetails.Where(detail =>
                         detail.IsFlashSaleItem &&
                         detail.FlashSaleItemId.HasValue &&
                         (detail.Quantity ?? 0) > 0))
            {
                if (detail.FlashSaleItem == null)
                {
                    throw new InvalidOperationException(
                        $"Không tìm thấy suất Flash Sale #{detail.FlashSaleItemId} để hoàn cho đơn #{orderId}.");
                }

                detail.FlashSaleItem.Sold = Math.Max(
                    0,
                    detail.FlashSaleItem.Sold - (detail.Quantity ?? 0));
            }
        }

        // Đồng bộ entity đang được tracking với UPDATE nguyên tử phía trên.
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

        var quantitiesByVariant = order.OrderDetails
            .Where(detail => detail.VariantId.HasValue && (detail.Quantity ?? 0) > 0)
            .GroupBy(detail => detail.VariantId!.Value)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(detail => detail.Quantity ?? 0)
            })
            .ToList();

        var reservationsByVariant = await _context.OrderReservations
            .Where(reservation => reservation.OrderId == orderId)
            .ToDictionaryAsync(reservation => reservation.VariantId, cancellationToken);

        foreach (var item in quantitiesByVariant)
        {
            _context.InventoryTransactions.Add(new InventoryTransactions
            {
                VariantId = item.VariantId,
                TransactionType = "RETURN_DAMAGED",
                Quantity = 0,
                ReferenceId = order.OrderId,
                TransactionDate = timestamp,
                Note = $"{reason}. Đơn #{order.OrderId}; {item.Quantity} sản phẩm không nhập lại tồn bán."
            });

            if (!reservationsByVariant.TryGetValue(item.VariantId, out var reservation))
            {
                reservation = new OrderReservations
                {
                    OrderId = order.OrderId,
                    VariantId = item.VariantId,
                    Quantity = item.Quantity,
                    ReservedAt = order.StockDeductedAt ?? timestamp
                };
                _context.OrderReservations.Add(reservation);
                reservationsByVariant[item.VariantId] = reservation;
            }

            reservation.Quantity = item.Quantity;
            reservation.Status = OrderReservationStatuses.Damaged;
            reservation.ReleasedAt = timestamp;
            reservation.Reason = reason;
        }

        order.IsStockDeducted = false;
        order.StockDeductedAt = null;

        return true;
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
            .FirstOrDefaultAsync(current => current.OrderId == orderId, cancellationToken);

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
}
