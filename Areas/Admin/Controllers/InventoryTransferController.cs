using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;
using TMDT_LT.Services.Inventory.Contracts;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers;

/// <summary>
/// Multi-SKU warehouse transfers. Stock is moved by FIFO lot allocation while
/// preserving original stock age, cost and serial traceability.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory/Transfers")]
public sealed class InventoryTransferController : Controller
{
    private const int MaxTransferLines = 100;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryTransferController> _logger;

    public InventoryTransferController(
        ApplicationDbContext context,
        ILogger<InventoryTransferController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("Preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewTransfer(
        [FromBody] InventoryTransferRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateTransferAsync(request, cancellationToken);
        if (!validation.Success)
        {
            return BadRequest(Fail(validation.Message));
        }

        var source = validation.SourceWarehouse!;
        var target = validation.TargetWarehouse!;
        var items = validation.Items!;
        var ids = items.Select(item => item.VariantId).ToList();
        var productRows = await _context.ProductVariants
            .AsNoTracking()
            .Where(item => ids.Contains(item.VariantId))
            .Select(item => new
            {
                item.VariantId,
                ProductName = item.Product.Name,
                item.Color,
                item.Storage,
                item.Ram
            })
            .ToDictionaryAsync(item => item.VariantId, cancellationToken);

        var sourceStockRows = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot => lot.WarehouseId == source.WarehouseId
                && ids.Contains(lot.VariantId)
                && lot.IsActive
                && !lot.IsDeleted)
            .GroupBy(lot => lot.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                OnHand = group.Sum(lot => lot.RemainingQuantity),
                Value = group.Sum(lot => lot.RemainingQuantity * lot.UnitCost)
            })
            .ToDictionaryAsync(item => item.VariantId, cancellationToken);
        var reserved = await LoadActiveReservationsAsync(source, ids, cancellationToken);

        var lines = items.Select(item =>
        {
            sourceStockRows.TryGetValue(item.VariantId, out var stock);
            productRows.TryGetValue(item.VariantId, out var product);
            int onHand = Math.Max(0, stock?.OnHand ?? 0);
            int reservedQuantity = Math.Max(0, reserved.GetValueOrDefault(item.VariantId));
            int available = Math.Max(0, onHand - reservedQuantity);
            decimal averageCost = onHand > 0
                ? RoundMoney((stock?.Value ?? 0m) / onHand)
                : 0m;
            return new
            {
                item.VariantId,
                sku = $"SKU-{item.VariantId:D6}",
                productName = product?.ProductName ?? $"Biến thể #{item.VariantId}",
                variantLabel = BuildVariantLabel(product?.Color, product?.Storage, product?.Ram),
                requestedQuantity = item.Quantity,
                onHand,
                reserved = reservedQuantity,
                available,
                averageCost,
                estimatedValue = RoundMoney(item.Quantity * averageCost),
                valid = available >= item.Quantity
            };
        }).ToList();

        return Json(new
        {
            success = lines.All(item => item.valid),
            sourceWarehouse = new
            {
                source.WarehouseId,
                source.WarehouseCode,
                source.WarehouseName
            },
            targetWarehouse = new
            {
                target.WarehouseId,
                target.WarehouseCode,
                target.WarehouseName
            },
            lineCount = lines.Count,
            totalQuantity = lines.Sum(item => item.requestedQuantity),
            estimatedValue = lines.Sum(item => item.estimatedValue),
            lines,
            message = lines.All(item => item.valid)
                ? "Đủ tồn khả dụng để điều chuyển."
                : "Có SKU không đủ tồn khả dụng sau khi trừ reservation."
        });
    }

    [HttpPost("Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitTransfer(
        [FromBody] InventoryTransferRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateTransferAsync(request, cancellationToken);
        if (!validation.Success)
        {
            return BadRequest(Fail(validation.Message));
        }

        var source = validation.SourceWarehouse!;
        var target = validation.TargetWarehouse!;
        var items = validation.Items!;
        var variantIds = items.Select(item => item.VariantId).ToList();

        bool hasOpenCount = await _context.Set<InventoryCountSession>()
            .AsNoTracking()
            .AnyAsync(session =>
                (session.WarehouseId == source.WarehouseId
                    || session.WarehouseId == target.WarehouseId)
                && (session.Status == InventoryCountSessionStatuses.Counting
                    || session.Status == InventoryCountSessionStatuses.PendingApproval)
                && session.Lines.Any(line => variantIds.Contains(line.VariantId)),
                cancellationToken);
        if (hasOpenCount)
        {
            return Conflict(Fail(
                "Một hoặc nhiều SKU đang nằm trong phiên kiểm kê mở ở kho nguồn/đích. Hãy hoàn tất hoặc hủy kiểm kê trước khi điều chuyển."));
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            int referenceId = await NextReferenceIdAsync(
                "WarehouseTransfer",
                cancellationToken);
            string transferCode = $"TRF-{DateTime.Now:yyyyMMdd}-{referenceId:D6}";
            DateTime now = DateTime.Now;
            int accountId = GetCurrentAccountId();
            var reserved = await LoadActiveReservationsAsync(source, variantIds, cancellationToken);
            var responseLines = new List<object>();
            var affectedVariantIds = new HashSet<int>();

            foreach (var requestedLine in items)
            {
                int sourceBefore = await GetWarehouseOnHandAsync(
                    source.WarehouseId,
                    requestedLine.VariantId,
                    cancellationToken);
                int targetBefore = await GetWarehouseOnHandAsync(
                    target.WarehouseId,
                    requestedLine.VariantId,
                    cancellationToken);
                int reservedQuantity = Math.Max(0, reserved.GetValueOrDefault(requestedLine.VariantId));
                int available = Math.Max(0, sourceBefore - reservedQuantity);
                if (available < requestedLine.Quantity)
                {
                    throw new InvalidOperationException(
                        $"SKU #{requestedLine.VariantId} chỉ còn {available} khả dụng tại kho nguồn, cần {requestedLine.Quantity}.");
                }

                var sourceLots = await _context.InventoryLots
                    .Where(lot => lot.WarehouseId == source.WarehouseId
                        && lot.VariantId == requestedLine.VariantId
                        && lot.RemainingQuantity > 0
                        && lot.IsActive
                        && !lot.IsDeleted)
                    .OrderBy(lot => lot.ReceivedDate)
                    .ThenBy(lot => lot.LotId)
                    .ToListAsync(cancellationToken);

                int remaining = requestedLine.Quantity;
                int sourceRunning = sourceBefore;
                int targetRunning = targetBefore;
                decimal transferredValue = 0m;
                var createdTargetLotIds = new List<int>();

                foreach (var sourceLot in sourceLots)
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    int take = Math.Min(sourceLot.RemainingQuantity, remaining);
                    if (take <= 0)
                    {
                        continue;
                    }

                    var serials = await _context.ProductSerials
                        .Where(serial => serial.VariantId == requestedLine.VariantId
                            && serial.LotId == sourceLot.LotId
                            && serial.Status == "InStock")
                        .OrderBy(serial => serial.CreatedDate)
                        .ThenBy(serial => serial.SerialId)
                        .Take(take)
                        .ToListAsync(cancellationToken);
                    if (serials.Count != take)
                    {
                        throw new InvalidOperationException(
                            $"Lô #{sourceLot.LotId} có {serials.Count} serial InStock nhưng cần chuyển {take}. Hãy đối soát serial trước.");
                    }

                    sourceLot.RemainingQuantity -= take;
                    remaining -= take;
                    sourceRunning -= take;
                    targetRunning += take;
                    decimal lineValue = RoundMoney(take * sourceLot.UnitCost);
                    transferredValue += lineValue;

                    var targetLot = new InventoryLots
                    {
                        Poid = sourceLot.Poid,
                        VariantId = sourceLot.VariantId,
                        SupplierId = sourceLot.SupplierId,
                        WarehouseId = target.WarehouseId,
                        ReceivedQuantity = take,
                        RemainingQuantity = take,
                        UnitCost = sourceLot.UnitCost,
                        // Luân chuyển nội bộ không làm mới tuổi hàng/FIFO.
                        ReceivedDate = sourceLot.ReceivedDate,
                        IsActive = true,
                        IsDeleted = false,
                        SourceType = InventoryLotSourceTypes.WarehouseTransfer,
                        SourceReference = $"{transferCode};FROM-LOT-{sourceLot.LotId}"
                    };
                    _context.InventoryLots.Add(targetLot);
                    await _context.SaveChangesAsync(cancellationToken);
                    createdTargetLotIds.Add(targetLot.LotId);

                    foreach (ProductSerials serial in serials)
                    {
                        serial.LotId = targetLot.LotId;
                    }

                    _context.InventoryTransactions.AddRange(
                        new InventoryTransactions
                        {
                            VariantId = requestedLine.VariantId,
                            TransactionType = "OUT_TRANSFER_FIFO",
                            Quantity = -take,
                            ReferenceId = referenceId,
                            ReferenceType = "WarehouseTransfer",
                            TransactionDate = now,
                            AccountId = accountId > 0 ? accountId : null,
                            WarehouseId = source.WarehouseId,
                            LotId = sourceLot.LotId,
                            QuantityBefore = sourceRunning + take,
                            QuantityAfter = sourceRunning,
                            ReasonCode = "WAREHOUSE_REBALANCE",
                            UnitCostSnapshot = sourceLot.UnitCost,
                            TotalCostSnapshot = lineValue,
                            ValueImpact = 0m,
                            Note = $"{transferCode}: chuyển sang {target.WarehouseCode}; {request!.Reason.Trim()}"
                        },
                        new InventoryTransactions
                        {
                            VariantId = requestedLine.VariantId,
                            TransactionType = "IN_TRANSFER_FIFO",
                            Quantity = take,
                            ReferenceId = referenceId,
                            ReferenceType = "WarehouseTransfer",
                            TransactionDate = now,
                            AccountId = accountId > 0 ? accountId : null,
                            WarehouseId = target.WarehouseId,
                            LotId = targetLot.LotId,
                            QuantityBefore = targetRunning - take,
                            QuantityAfter = targetRunning,
                            ReasonCode = "WAREHOUSE_REBALANCE",
                            UnitCostSnapshot = sourceLot.UnitCost,
                            TotalCostSnapshot = lineValue,
                            ValueImpact = 0m,
                            Note = $"{transferCode}: nhận từ {source.WarehouseCode}, lô nguồn #{sourceLot.LotId}; {request!.Reason.Trim()}"
                        });
                }

                if (remaining > 0)
                {
                    throw new InvalidOperationException(
                        $"Không đủ lô FIFO để hoàn tất điều chuyển SKU #{requestedLine.VariantId}.");
                }

                affectedVariantIds.Add(requestedLine.VariantId);
                responseLines.Add(new
                {
                    variantId = requestedLine.VariantId,
                    quantity = requestedLine.Quantity,
                    sourceBefore,
                    sourceAfter = sourceRunning,
                    targetBefore,
                    targetAfter = targetRunning,
                    transferredValue = RoundMoney(transferredValue),
                    targetLotIds = createdTargetLotIds
                });
            }

            foreach (int variantId in affectedVariantIds)
            {
                int aggregateStock = await _context.InventoryLots
                    .Where(lot => lot.VariantId == variantId
                        && lot.IsActive
                        && !lot.IsDeleted)
                    .SumAsync(lot => (int?)lot.RemainingQuantity, cancellationToken) ?? 0;
                var variant = await _context.ProductVariants
                    .FirstOrDefaultAsync(item => item.VariantId == variantId, cancellationToken);
                if (variant != null)
                {
                    variant.Stock = Math.Max(0, aggregateStock);
                    variant.UpdatedDate = now;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                referenceId,
                transferCode,
                sourceWarehouseId = source.WarehouseId,
                sourceWarehouseName = source.WarehouseName,
                targetWarehouseId = target.WarehouseId,
                targetWarehouseName = target.WarehouseName,
                lineCount = items.Count,
                totalQuantity = items.Sum(item => item.Quantity),
                lines = responseLines,
                postedAt = now,
                message = "Đã điều chuyển FIFO, di chuyển serial và ghi sổ hai đầu kho. Tổng tồn toàn hệ thống không đổi."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "inventory operations warehouse transfer failed.");
            return BadRequest(Fail("Không thể điều chuyển kho: " + exception.Message));
        }
    }

    [HttpGet("Recent")]
    public async Task<IActionResult> RecentTransfers(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 50);
        var rows = await _context.InventoryTransactions
            .AsNoTracking()
            .Where(item => item.ReferenceType == "WarehouseTransfer"
                && item.ReferenceId.HasValue)
            .OrderByDescending(item => item.TransactionDate)
            .ThenByDescending(item => item.TransactionId)
            .Take(take * 20)
            .Select(item => new
            {
                item.ReferenceId,
                item.TransactionType,
                item.Quantity,
                item.TransactionDate,
                item.VariantId,
                ProductName = item.Variant.Product.Name,
                item.WarehouseId,
                WarehouseName = item.Warehouse != null
                    ? item.Warehouse.WarehouseName
                    : "Kho chưa xác định",
                item.TotalCostSnapshot,
                item.Note
            })
            .ToListAsync(cancellationToken);

        var transfers = rows
            .GroupBy(item => item.ReferenceId!.Value)
            .OrderByDescending(group => group.Max(item => item.TransactionDate))
            .Take(take)
            .Select(group =>
            {
                var outbound = group.FirstOrDefault(item => item.TransactionType.StartsWith("OUT_TRANSFER"));
                var inbound = group.FirstOrDefault(item => item.TransactionType.StartsWith("IN_TRANSFER"));
                return new
                {
                    referenceId = group.Key,
                    transferCode = ExtractOperationCode(group.Select(item => item.Note).FirstOrDefault()),
                    transactionDate = group.Max(item => item.TransactionDate),
                    sourceWarehouseId = outbound?.WarehouseId,
                    sourceWarehouseName = outbound?.WarehouseName,
                    targetWarehouseId = inbound?.WarehouseId,
                    targetWarehouseName = inbound?.WarehouseName,
                    lineCount = group.Select(item => item.VariantId).Distinct().Count(),
                    totalQuantity = group.Where(item => item.Quantity > 0).Sum(item => item.Quantity),
                    totalValue = group
                        .Where(item => item.Quantity > 0)
                        .Sum(item => item.TotalCostSnapshot ?? 0m),
                    products = string.Join(", ", group
                        .Select(item => item.ProductName)
                        .Distinct()
                        .Take(4))
                };
            })
            .ToList();

        return Json(new { success = true, transfers });
    }


    private async Task<TransferValidationResult> ValidateTransferAsync(
        InventoryTransferRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return TransferValidationResult.Fail("Dữ liệu điều chuyển không hợp lệ.");
        }
        if (request.SourceWarehouseId <= 0
            || request.TargetWarehouseId <= 0
            || request.SourceWarehouseId == request.TargetWarehouseId)
        {
            return TransferValidationResult.Fail("Kho nguồn và kho đích phải hợp lệ, khác nhau.");
        }
        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length < 5)
        {
            return TransferValidationResult.Fail("Cần nhập lý do điều chuyển tối thiểu 5 ký tự.");
        }

        var items = request.Items
            .Where(item => item.VariantId > 0 && item.Quantity > 0)
            .GroupBy(item => item.VariantId)
            .Select(group => new InventoryTransferItemRequest
            {
                VariantId = group.Key,
                Quantity = group.Sum(item => item.Quantity)
            })
            .Take(MaxTransferLines + 1)
            .ToList();
        if (items.Count == 0)
        {
            return TransferValidationResult.Fail("Phiếu điều chuyển chưa có SKU.");
        }
        if (items.Count > MaxTransferLines)
        {
            return TransferValidationResult.Fail(
                $"Một lần điều chuyển tối đa {MaxTransferLines} SKU.");
        }

        var warehouses = await _context.Warehouses
            .AsNoTracking()
            .Where(item => item.IsActive
                && (item.WarehouseId == request.SourceWarehouseId
                    || item.WarehouseId == request.TargetWarehouseId))
            .ToListAsync(cancellationToken);
        Warehouses? source = warehouses.FirstOrDefault(item =>
            item.WarehouseId == request.SourceWarehouseId);
        Warehouses? target = warehouses.FirstOrDefault(item =>
            item.WarehouseId == request.TargetWarehouseId);
        if (source == null || target == null)
        {
            return TransferValidationResult.Fail("Kho nguồn hoặc kho đích không hoạt động.");
        }

        var ids = items.Select(item => item.VariantId).ToList();
        int validVariantCount = await _context.ProductVariants
            .AsNoTracking()
            .CountAsync(item => ids.Contains(item.VariantId)
                && item.IsActive == true,
                cancellationToken);
        if (validVariantCount != ids.Count)
        {
            return TransferValidationResult.Fail("Danh sách có SKU không tồn tại hoặc ngừng kinh doanh.");
        }

        return TransferValidationResult.Ok(source, target, items);
    }

    private async Task<Dictionary<int, int>> LoadActiveReservationsAsync(
        Warehouses warehouse,
        IReadOnlyCollection<int> variantIds,
        CancellationToken cancellationToken)
    {
        if (!warehouse.IsPrimary || variantIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        DateTime now = DateTime.Now;
        return await _context.Set<OrderReservations>()
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
            .ToDictionaryAsync(item => item.VariantId, item => item.Quantity, cancellationToken);
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
            ?? throw new InvalidOperationException("Chưa có kho hoạt động để vận hành.");
    }

    private async Task<int> GetWarehouseOnHandAsync(
        int warehouseId,
        int variantId,
        CancellationToken cancellationToken)
    {
        return await _context.InventoryLots
            .Where(lot => lot.WarehouseId == warehouseId
                && lot.VariantId == variantId
                && lot.IsActive
                && !lot.IsDeleted)
            .SumAsync(lot => (int?)lot.RemainingQuantity, cancellationToken) ?? 0;
    }

    private async Task<int> NextReferenceIdAsync(
        string referenceType,
        CancellationToken cancellationToken)
    {
        int current = await _context.InventoryTransactions
            .Where(item => item.ReferenceType == referenceType
                && item.ReferenceId.HasValue)
            .MaxAsync(item => (int?)item.ReferenceId, cancellationToken) ?? 0;
        return checked(current + 1);
    }

    private int GetCurrentAccountId()
    {
        string? value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out int accountId) ? accountId : 0;
    }

    private static string BuildVariantLabel(
        string? color,
        string? storage,
        string? ram)
    {
        string label = string.Join(" · ", new[] { color, storage, ram }
            .Where(item => !string.IsNullOrWhiteSpace(item)));
        return string.IsNullOrWhiteSpace(label) ? "Biến thể mặc định" : label;
    }

    private static string ExtractOperationCode(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return "TRF";
        }
        int separator = note.IndexOf(':');
        return separator > 0 ? note[..separator] : note;
    }

    private static decimal RoundMoney(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static object Fail(string message) => new
    {
        success = false,
        message
    };

    private sealed record TransferValidationResult(
        bool Success,
        string Message,
        Warehouses? SourceWarehouse,
        Warehouses? TargetWarehouse,
        List<InventoryTransferItemRequest>? Items)
    {
        public static TransferValidationResult Ok(
            Warehouses source,
            Warehouses target,
            List<InventoryTransferItemRequest> items)
            => new(true, string.Empty, source, target, items);

        public static TransferValidationResult Fail(string message)
            => new(false, message, null, null, null);
    }
}
