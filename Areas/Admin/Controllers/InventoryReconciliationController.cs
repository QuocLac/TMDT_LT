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
/// Inventory integrity checks and controlled repair of aggregate stock
/// snapshots. Lot and serial discrepancies are reported for physical review,
/// never repaired silently.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory/Reconciliation")]
public sealed class InventoryReconciliationController : Controller
{
    private const int AgedStockDays = 90;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryReconciliationController> _logger;

    public InventoryReconciliationController(
        ApplicationDbContext context,
        ILogger<InventoryReconciliationController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("Health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var activeLots = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot => lot.IsActive && !lot.IsDeleted)
            .Select(lot => new
            {
                lot.LotId,
                lot.VariantId,
                lot.WarehouseId,
                lot.ReceivedQuantity,
                lot.RemainingQuantity,
                lot.UnitCost,
                lot.ReceivedDate
            })
            .ToListAsync(cancellationToken);

        var variantSnapshots = await _context.ProductVariants
            .AsNoTracking()
            .Select(item => new
            {
                item.VariantId,
                Stock = item.Stock ?? 0,
                ProductName = item.Product.Name
            })
            .ToListAsync(cancellationToken);

        var lotStockByVariant = activeLots
            .GroupBy(item => item.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => Math.Max(0, item.RemainingQuantity)));
        var allStockDrifts = variantSnapshots
            .Where(item => item.Stock != lotStockByVariant.GetValueOrDefault(item.VariantId))
            .Select(item => new
            {
                item.VariantId,
                item.ProductName,
                snapshotStock = item.Stock,
                lotStock = lotStockByVariant.GetValueOrDefault(item.VariantId),
                difference = lotStockByVariant.GetValueOrDefault(item.VariantId) - item.Stock
            })
            .OrderByDescending(item => Math.Abs(item.difference))
            .ToList();
        var stockDrifts = allStockDrifts.Take(30).ToList();

        var serialRows = await _context.ProductSerials
            .AsNoTracking()
            .Where(serial => serial.Status == "InStock")
            .GroupBy(serial => serial.LotId)
            .Select(group => new
            {
                LotId = group.Key,
                Quantity = group.Count()
            })
            .ToListAsync(cancellationToken);
        var serialByLot = serialRows.ToDictionary(item => item.LotId, item => item.Quantity);
        var allSerialMismatches = activeLots
            .Where(lot => serialByLot.GetValueOrDefault(lot.LotId) != lot.RemainingQuantity)
            .Select(lot => new
            {
                lot.LotId,
                lot.VariantId,
                lot.WarehouseId,
                lot.RemainingQuantity,
                inStockSerialCount = serialByLot.GetValueOrDefault(lot.LotId),
                difference = serialByLot.GetValueOrDefault(lot.LotId) - lot.RemainingQuantity
            })
            .OrderByDescending(item => Math.Abs(item.difference))
            .ToList();
        var serialMismatches = allSerialMismatches.Take(30).ToList();

        int invalidLotCount = activeLots.Count(lot =>
            lot.WarehouseId <= 0
            || lot.ReceivedQuantity < 0
            || lot.RemainingQuantity < 0
            || lot.RemainingQuantity > lot.ReceivedQuantity
            || lot.UnitCost < 0m);
        DateTime agedThreshold = DateTime.Now.AddDays(-AgedStockDays);
        int agedLotCount = activeLots.Count(lot =>
            lot.RemainingQuantity > 0 && lot.ReceivedDate < agedThreshold);
        var warehouseGroups = activeLots
            .GroupBy(lot => lot.WarehouseId)
            .Select(group => new
            {
                warehouseId = group.Key,
                onHand = group.Sum(item => Math.Max(0, item.RemainingQuantity)),
                inventoryValue = RoundMoney(group.Sum(item => Math.Max(0, item.RemainingQuantity) * item.UnitCost)),
                lotCount = group.Count(item => item.RemainingQuantity > 0),
                agedLotCount = group.Count(item => item.RemainingQuantity > 0 && item.ReceivedDate < agedThreshold)
            })
            .OrderBy(item => item.warehouseId)
            .ToList();

        int openCountSessions = await _context.Set<InventoryCountSession>()
            .AsNoTracking()
            .CountAsync(item => item.Status == InventoryCountSessionStatuses.Counting
                || item.Status == InventoryCountSessionStatuses.PendingApproval,
                cancellationToken);
        int activeReservations = await _context.Set<OrderReservations>()
            .AsNoTracking()
            .Where(item => item.Status == OrderReservationStatuses.Reserved
                && (!item.ExpiresAt.HasValue || item.ExpiresAt > DateTime.Now))
            .SumAsync(item => (int?)item.Quantity, cancellationToken) ?? 0;

        var recentTransactions = await _context.InventoryTransactions
            .AsNoTracking()
            .OrderByDescending(item => item.TransactionDate)
            .ThenByDescending(item => item.TransactionId)
            .Take(10)
            .Select(item => new
            {
                item.TransactionId,
                item.TransactionType,
                item.Quantity,
                item.TransactionDate,
                item.ReferenceType,
                item.ReferenceId,
                item.Note
            })
            .ToListAsync(cancellationToken);

        int criticalIssueCount = allStockDrifts.Count + allSerialMismatches.Count + invalidLotCount;
        return Json(new
        {
            success = true,
            generatedAt = DateTime.Now,
            healthy = criticalIssueCount == 0,
            criticalIssueCount,
            activeLotCount = activeLots.Count,
            totalOnHand = activeLots.Sum(item => Math.Max(0, item.RemainingQuantity)),
            totalInventoryValue = RoundMoney(activeLots.Sum(item => Math.Max(0, item.RemainingQuantity) * item.UnitCost)),
            stockDriftCount = allStockDrifts.Count,
            serialMismatchCount = allSerialMismatches.Count,
            invalidLotCount,
            agedLotCount,
            openCountSessions,
            activeReservations,
            stockDrifts,
            serialMismatches,
            warehouses = warehouseGroups,
            recentTransactions,
            notes = new[]
            {
                "ProductVariants.Stock là snapshot tổng; nguồn sự thật là tổng RemainingQuantity của lô hoạt động.",
                "Serial mismatch không tự sửa vì cần đối chiếu vật lý/IMEI; chỉ hiển thị để thủ kho xử lý qua kiểm kê.",
                "Reservation hiện chưa có WarehouseId và được bảo vệ tại kho chính trong giai đoạn hiện tại."
            }
        });
    }

    [HttpPost("RepairVariantSnapshots")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RepairVariantSnapshots(
        [FromBody] InventorySnapshotRepairRequest? request,
        CancellationToken cancellationToken)
    {
        string reason = (request?.Reason ?? string.Empty).Trim();
        if (reason.Length < 5)
        {
            return BadRequest(Fail("Cần nhập lý do đối soát tối thiểu 5 ký tự."));
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var variants = await _context.ProductVariants
                .ToListAsync(cancellationToken);
            var lotRows = await _context.InventoryLots
                .AsNoTracking()
                .Where(lot => lot.IsActive && !lot.IsDeleted)
                .GroupBy(lot => lot.VariantId)
                .Select(group => new
                {
                    VariantId = group.Key,
                    Quantity = group.Sum(lot => lot.RemainingQuantity)
                })
                .ToListAsync(cancellationToken);
            var lotByVariant = lotRows.ToDictionary(item => item.VariantId, item => Math.Max(0, item.Quantity));
            int accountId = GetCurrentAccountId();
            DateTime now = DateTime.Now;
            int repaired = 0;

            foreach (ProductVariants variant in variants)
            {
                int before = Math.Max(0, variant.Stock ?? 0);
                int after = lotByVariant.GetValueOrDefault(variant.VariantId);
                if (before == after)
                {
                    continue;
                }

                variant.Stock = after;
                variant.UpdatedDate = now;
                repaired++;
                _context.InventoryTransactions.Add(new InventoryTransactions
                {
                    VariantId = variant.VariantId,
                    TransactionType = "SNAPSHOT_RECONCILIATION",
                    Quantity = after - before,
                    ReferenceType = "InventorySnapshotRepair",
                    TransactionDate = now,
                    AccountId = accountId > 0 ? accountId : null,
                    QuantityBefore = before,
                    QuantityAfter = after,
                    ReasonCode = "SYSTEM_RECONCILIATION",
                    ValueImpact = 0m,
                    Note = $"Đồng bộ ProductVariants.Stock theo tổng lô hoạt động. Lý do: {reason}"
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Json(new
            {
                success = true,
                scannedVariantCount = variants.Count,
                repairedVariantCount = repaired,
                message = $"Đã đồng bộ {repaired} snapshot tồn tổng theo sổ lô."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "inventory operations variant snapshot repair failed.");
            return StatusCode(500, Fail("Không thể đồng bộ snapshot tồn: " + exception.Message));
        }
    }


    private int GetCurrentAccountId()
    {
        string? value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out int accountId) ? accountId : 0;
    }

    private static decimal RoundMoney(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static object Fail(string message) => new
    {
        success = false,
        message
    };

}
