using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers;

/// <summary>
/// Inventory dashboard, stock visibility and inventory-ledger queries.
/// Write operations are handled by dedicated receiving, counting, transfer,
/// distribution and reconciliation controllers.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory")]
public sealed class InventoryController : Controller
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int LowStockThreshold = 5;

    private readonly ApplicationDbContext _context;

    public InventoryController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public IActionResult Dashboard()
    {
        return View("~/Areas/Admin/Views/Inventory/Dashboard.cshtml");
    }

    [HttpGet("Bootstrap")]
    public async Task<IActionResult> Bootstrap(CancellationToken cancellationToken)
    {
        var warehouses = await _context.Warehouses
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderByDescending(item => item.IsPrimary)
            .ThenBy(item => item.WarehouseName)
            .Select(item => new
            {
                warehouseId = item.WarehouseId,
                warehouseCode = item.WarehouseCode,
                warehouseName = item.WarehouseName,
                address = item.Address,
                isPrimary = item.IsPrimary
            })
            .ToListAsync(cancellationToken);

        var reasons = InventoryCountReasonCodes.Labels
            .Select(item => new { code = item.Key, label = item.Value })
            .ToList();

        return Json(new
        {
            success = true,
            warehouses,
            reasons,
            statuses = new[]
            {
                new { code = InventoryCountSessionStatuses.Counting, label = "Đang kiểm đếm" },
                new { code = InventoryCountSessionStatuses.PendingApproval, label = "Chờ duyệt" },
                new { code = InventoryCountSessionStatuses.Posted, label = "Đã ghi sổ" },
                new { code = InventoryCountSessionStatuses.Cancelled, label = "Đã hủy" }
            },
            generatedAt = DateTime.Now
        });
    }

    [HttpGet("Stock")]
    public async Task<IActionResult> Stock(
        int? warehouseId,
        string? q,
        string? stockScope,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, MaxPageSize);

        Warehouses warehouse;
        try
        {
            warehouse = await ResolveWarehouseAsync(warehouseId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(Fail(exception.Message));
        }

        string keyword = (q ?? string.Empty).Trim();
        int? exactVariantId = int.TryParse(keyword, out int parsedVariantId)
            ? parsedVariantId
            : null;

        var variantsQuery = _context.ProductVariants
            .AsNoTracking()
            .Where(item => item.IsActive == true && item.Product != null);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            variantsQuery = variantsQuery.Where(item =>
                (exactVariantId.HasValue && item.VariantId == exactVariantId.Value)
                || item.Product.Name.Contains(keyword)
                || (item.Color != null && item.Color.Contains(keyword))
                || (item.Storage != null && item.Storage.Contains(keyword))
                || (item.Ram != null && item.Ram.Contains(keyword))
                || item.Product.Brand.BrandName.Contains(keyword)
                || item.Product.Category.CategoryName.Contains(keyword));
        }

        string normalizedScope = (stockScope ?? "all").Trim().ToLowerInvariant();
        if (normalizedScope is "available" or "low" or "out" or "drift")
        {
            variantsQuery = normalizedScope switch
            {
                "available" => variantsQuery.Where(item =>
                    (_context.InventoryLots
                        .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                            && lot.VariantId == item.VariantId
                            && lot.IsActive
                            && !lot.IsDeleted)
                        .Sum(lot => (int?)lot.RemainingQuantity) ?? 0) > 0),
                "low" => variantsQuery.Where(item =>
                    (_context.InventoryLots
                        .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                            && lot.VariantId == item.VariantId
                            && lot.IsActive
                            && !lot.IsDeleted)
                        .Sum(lot => (int?)lot.RemainingQuantity) ?? 0) > 0
                    && (_context.InventoryLots
                        .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                            && lot.VariantId == item.VariantId
                            && lot.IsActive
                            && !lot.IsDeleted)
                        .Sum(lot => (int?)lot.RemainingQuantity) ?? 0) <= LowStockThreshold),
                "out" => variantsQuery.Where(item =>
                    !(_context.InventoryLots.Any(lot => lot.WarehouseId == warehouse.WarehouseId
                        && lot.VariantId == item.VariantId
                        && lot.RemainingQuantity > 0
                        && lot.IsActive
                        && !lot.IsDeleted))),
                "drift" => variantsQuery.Where(item =>
                    (_context.InventoryLots
                        .Where(lot => lot.VariantId == item.VariantId
                            && lot.IsActive
                            && !lot.IsDeleted)
                        .Sum(lot => (int?)lot.RemainingQuantity) ?? 0) != (item.Stock ?? 0)),
                _ => variantsQuery
            };
        }

        int totalItems = await variantsQuery.CountAsync(cancellationToken);
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Min(page, totalPages);

        var pageItems = await variantsQuery
            .OrderBy(item => item.Product.Name)
            .ThenBy(item => item.VariantId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new
            {
                item.VariantId,
                ProductName = item.Product.Name,
                item.Color,
                item.Storage,
                item.Ram,
                Price = item.Price ?? 0m,
                ImageUrl = item.ImageUrl ?? item.Product.MainImage,
                BrandName = item.Product.Brand.BrandName,
                CategoryName = item.Product.Category.CategoryName,
                AggregateStockSnapshot = item.Stock ?? 0
            })
            .ToListAsync(cancellationToken);

        var variantIds = pageItems.Select(item => item.VariantId).ToList();
        DateTime now = DateTime.Now;

        var warehouseLotRows = variantIds.Count == 0
            ? new List<WarehouseLotSummaryRow>()
            : await _context.InventoryLots
                .AsNoTracking()
                .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                    && variantIds.Contains(lot.VariantId)
                    && lot.IsActive
                    && !lot.IsDeleted)
                .GroupBy(lot => lot.VariantId)
                .Select(group => new WarehouseLotSummaryRow
                {
                    VariantId = group.Key,
                    OnHand = group.Sum(lot => lot.RemainingQuantity),
                    InventoryValue = group.Sum(lot => lot.RemainingQuantity * lot.UnitCost),
                    LotCount = group.Count(lot => lot.RemainingQuantity > 0),
                    OldestReceivedDate = group
                        .Where(lot => lot.RemainingQuantity > 0)
                        .Min(lot => (DateTime?)lot.ReceivedDate)
                })
                .ToListAsync(cancellationToken);

        var lotByVariant = warehouseLotRows.ToDictionary(item => item.VariantId);

        // Reservation hiện tại chưa lưu WarehouseId. Trong giai đoạn chuyển tiếp,
        // chỉ quy reservation đang hoạt động về kho chính để không trừ lặp ở nhiều kho.
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
                .ToDictionaryAsync(item => item.VariantId, item => item.Quantity, cancellationToken);

        var aggregateByVariant = variantIds.Count == 0
            ? new Dictionary<int, int>()
            : await _context.InventoryLots
                .AsNoTracking()
                .Where(lot => variantIds.Contains(lot.VariantId)
                    && lot.IsActive
                    && !lot.IsDeleted)
                .GroupBy(lot => lot.VariantId)
                .Select(group => new
                {
                    VariantId = group.Key,
                    Quantity = group.Sum(lot => lot.RemainingQuantity)
                })
                .ToDictionaryAsync(item => item.VariantId, item => item.Quantity, cancellationToken);

        var items = pageItems.Select(item =>
        {
            lotByVariant.TryGetValue(item.VariantId, out WarehouseLotSummaryRow? lot);
            int onHand = Math.Max(0, lot?.OnHand ?? 0);
            int reserved = Math.Max(0, reservationByVariant.GetValueOrDefault(item.VariantId));
            int available = Math.Max(0, onHand - reserved);
            decimal inventoryValue = Math.Max(0m, lot?.InventoryValue ?? 0m);
            decimal averageCost = onHand > 0 ? inventoryValue / onHand : 0m;
            int aggregateOnHand = Math.Max(0, aggregateByVariant.GetValueOrDefault(item.VariantId));
            int stockDrift = aggregateOnHand - Math.Max(0, item.AggregateStockSnapshot);

            return new
            {
                variantId = item.VariantId,
                sku = $"SKU-{item.VariantId:D6}",
                productName = item.ProductName,
                variantLabel = BuildVariantLabel(item.Color, item.Storage, item.Ram),
                imageUrl = string.IsNullOrWhiteSpace(item.ImageUrl)
                    ? "/images/products/default-product.png"
                    : item.ImageUrl,
                item.BrandName,
                item.CategoryName,
                item.Price,
                onHand,
                reserved,
                available,
                averageCost,
                inventoryValue,
                lotCount = lot?.LotCount ?? 0,
                oldestStockDays = lot?.OldestReceivedDate.HasValue == true
                    ? Math.Max(0, (now.Date - lot.OldestReceivedDate.Value.Date).Days)
                    : 0,
                aggregateOnHand,
                aggregateStockSnapshot = Math.Max(0, item.AggregateStockSnapshot),
                stockDrift
            };
        }).ToList();

        var allWarehouseLots = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                && lot.IsActive
                && !lot.IsDeleted)
            .GroupBy(lot => lot.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                OnHand = group.Sum(lot => lot.RemainingQuantity),
                InventoryValue = group.Sum(lot => lot.RemainingQuantity * lot.UnitCost)
            })
            .ToListAsync(cancellationToken);

        int totalOnHand = allWarehouseLots.Sum(item => Math.Max(0, item.OnHand));
        decimal totalInventoryValue = allWarehouseLots.Sum(item => Math.Max(0m, item.InventoryValue));
        int lowStockCount = allWarehouseLots.Count(item => item.OnHand > 0 && item.OnHand <= LowStockThreshold);

        int totalReserved = warehouse.IsPrimary
            ? await _context.Set<OrderReservations>()
                .AsNoTracking()
                .Where(item => item.Status == OrderReservationStatuses.Reserved
                    && (!item.ExpiresAt.HasValue || item.ExpiresAt > now))
                .SumAsync(item => (int?)item.Quantity, cancellationToken) ?? 0
            : 0;

        int pendingCounts = await _context.Set<InventoryCountSession>()
            .AsNoTracking()
            .CountAsync(item => item.WarehouseId == warehouse.WarehouseId
                && (item.Status == InventoryCountSessionStatuses.Counting
                    || item.Status == InventoryCountSessionStatuses.PendingApproval),
                cancellationToken);

        return Json(new
        {
            success = true,
            warehouse = new
            {
                warehouse.WarehouseId,
                warehouse.WarehouseCode,
                warehouse.WarehouseName,
                warehouse.IsPrimary
            },
            page,
            pageSize,
            totalItems,
            totalPages,
            items,
            summary = new
            {
                totalOnHand,
                totalReserved = Math.Max(0, totalReserved),
                totalAvailable = Math.Max(0, totalOnHand - Math.Max(0, totalReserved)),
                totalInventoryValue,
                lowStockCount,
                pendingCounts,
                activeVariantCount = allWarehouseLots.Count(item => item.OnHand > 0)
            },
            reservationNote = warehouse.IsPrimary
                ? "Reservation hiện tại chưa có WarehouseId và được quy về kho chính trong giai đoạn chuyển tiếp."
                : "Reservation chưa phân bổ theo kho nên không bị trừ lặp tại kho phụ."
        });
    }

    [HttpGet("Transactions")]
    public async Task<IActionResult> Transactions(
        int? warehouseId,
        int take = 15,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 50);
        var query = _context.InventoryTransactions
            .AsNoTracking()
            .Where(item => item.WarehouseId.HasValue);

        if (warehouseId.HasValue && warehouseId.Value > 0)
        {
            query = query.Where(item => item.WarehouseId == warehouseId.Value);
        }

        var transactions = await query
            .OrderByDescending(item => item.TransactionDate)
            .ThenByDescending(item => item.TransactionId)
            .Take(take)
            .Select(item => new
            {
                transactionId = item.TransactionId,
                variantId = item.VariantId,
                sku = $"SKU-{item.VariantId:D6}",
                productName = item.Variant.Product.Name,
                transactionType = item.TransactionType,
                quantity = item.Quantity,
                quantityBefore = item.QuantityBefore,
                quantityAfter = item.QuantityAfter,
                reasonCode = item.ReasonCode,
                valueImpact = item.ValueImpact,
                transactionDate = item.TransactionDate,
                warehouseId = item.WarehouseId,
                warehouseName = item.Warehouse != null
                    ? item.Warehouse.WarehouseName
                    : "Kho chưa xác định",
                referenceType = item.ReferenceType,
                referenceId = item.ReferenceId,
                note = item.Note
            })
            .ToListAsync(cancellationToken);

        return Json(new { success = true, transactions });
    }

    private async Task<Warehouses> ResolveWarehouseAsync(
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        Warehouses? warehouse = null;
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
    private static object Fail(string message) => new { success = false, message };

    private sealed class WarehouseLotSummaryRow
    {
        public int VariantId { get; set; }
        public int OnHand { get; set; }
        public decimal InventoryValue { get; set; }
        public int LotCount { get; set; }
        public DateTime? OldestReceivedDate { get; set; }
    }
}
