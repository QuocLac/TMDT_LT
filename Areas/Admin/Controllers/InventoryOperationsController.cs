using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers;

/// <summary>
/// Inventory operations workspace and demand-based replenishment queries.
/// Transfer, distribution and reconciliation writes are isolated in dedicated
/// controllers.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory/Operations")]
public sealed class InventoryOperationsController : Controller
{
    private const int DefaultPageSize = 12;
    private const int MaxPageSize = 50;
    private const int LowStockThreshold = 5;
    private const int AgedStockDays = 90;

    private static readonly string[] RecognizedDemandStatuses =
    {
        "Completed",
        "Delivered",
        "Đã giao",
        "Đã hoàn thành",
        "Hoàn thành"
    };

    private readonly ApplicationDbContext _context;

    public InventoryOperationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("")]
    public IActionResult Workspace()
    {
        return View("~/Areas/Admin/Views/Inventory/Operations.cshtml");
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

        var stores = await _context.Stores
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.StoreType)
            .ThenBy(item => item.StoreName)
            .Select(item => new
            {
                storeId = item.StoreId,
                storeCode = item.StoreCode,
                storeName = item.StoreName,
                storeType = item.StoreType,
                address = item.Address,
                contactPerson = item.ContactPerson,
                phone = item.Phone
            })
            .ToListAsync(cancellationToken);

        var categories = await _context.Categories
            .AsNoTracking()
            .Where(item => item.IsActive == true)
            .OrderBy(item => item.CategoryName)
            .Select(item => new
            {
                categoryId = item.CategoryId,
                categoryName = item.CategoryName
            })
            .ToListAsync(cancellationToken);

        var brands = await _context.Brands
            .AsNoTracking()
            .OrderBy(item => item.BrandName)
            .Select(item => new
            {
                brandId = item.BrandId,
                brandName = item.BrandName
            })
            .ToListAsync(cancellationToken);

        return Json(new
        {
            success = true,
            warehouses,
            stores,
            categories,
            brands,
            defaults = new
            {
                demandWindowDays = 30,
                leadTimeDays = 7,
                safetyDays = 3,
                fallbackTargetQuantity = 10,
                lowStockThreshold = LowStockThreshold,
                agedStockDays = AgedStockDays
            },
            generatedAt = DateTime.Now
        });
    }

    [HttpGet("Products")]
    public async Task<IActionResult> Products(
        int? warehouseId,
        string? q,
        int? categoryId,
        int? brandId,
        string? stockScope,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 6, MaxPageSize);

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

        var query = _context.ProductVariants
            .AsNoTracking()
            .Where(item => item.IsActive == true && item.Product != null);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(item =>
                (exactVariantId.HasValue && item.VariantId == exactVariantId.Value)
                || item.Product.Name.Contains(keyword)
                || (item.Color != null && item.Color.Contains(keyword))
                || (item.Storage != null && item.Storage.Contains(keyword))
                || (item.Ram != null && item.Ram.Contains(keyword))
                || item.Product.Brand.BrandName.Contains(keyword)
                || item.Product.Category.CategoryName.Contains(keyword));
        }

        if (categoryId.HasValue && categoryId.Value > 0)
        {
            query = query.Where(item => item.Product.CategoryId == categoryId.Value);
        }

        if (brandId.HasValue && brandId.Value > 0)
        {
            query = query.Where(item => item.Product.BrandId == brandId.Value);
        }

        string normalizedScope = (stockScope ?? "all").Trim().ToLowerInvariant();
        if (normalizedScope is "available" or "low" or "out")
        {
            query = normalizedScope switch
            {
                "available" => query.Where(item =>
                    (_context.InventoryLots
                        .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                            && lot.VariantId == item.VariantId
                            && lot.IsActive
                            && !lot.IsDeleted)
                        .Sum(lot => (int?)lot.RemainingQuantity) ?? 0) > 0),
                "low" => query.Where(item =>
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
                "out" => query.Where(item => !_context.InventoryLots.Any(lot =>
                    lot.WarehouseId == warehouse.WarehouseId
                    && lot.VariantId == item.VariantId
                    && lot.RemainingQuantity > 0
                    && lot.IsActive
                    && !lot.IsDeleted)),
                _ => query
            };
        }

        int totalItems = await query.CountAsync(cancellationToken);
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Min(page, totalPages);

        var pageItems = await query
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
                CostPrice = item.CostPrice ?? 0m,
                ImageUrl = item.ImageUrl ?? item.Product.MainImage,
                BrandName = item.Product.Brand.BrandName,
                CategoryName = item.Product.Category.CategoryName
            })
            .ToListAsync(cancellationToken);

        var variantIds = pageItems.Select(item => item.VariantId).ToList();
        var lotRows = variantIds.Count == 0
            ? new List<OperationsLotSummaryRow>()
            : await _context.InventoryLots
                .AsNoTracking()
                .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                    && variantIds.Contains(lot.VariantId)
                    && lot.IsActive
                    && !lot.IsDeleted)
                .GroupBy(lot => lot.VariantId)
                .Select(group => new OperationsLotSummaryRow
                {
                    VariantId = group.Key,
                    OnHand = group.Sum(lot => lot.RemainingQuantity),
                    InventoryValue = group.Sum(lot => lot.RemainingQuantity * lot.UnitCost),
                    LotCount = group.Count(lot => lot.RemainingQuantity > 0),
                    OldestReceivedDate = group
                        .Where(lot => lot.RemainingQuantity > 0)
                        .Min(lot => (DateTime?)lot.ReceivedDate),
                    LatestUnitCost = group
                        .OrderByDescending(lot => lot.ReceivedDate)
                        .ThenByDescending(lot => lot.LotId)
                        .Select(lot => lot.UnitCost)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

        var lotByVariant = lotRows.ToDictionary(item => item.VariantId);
        var reservedByVariant = await LoadActiveReservationsAsync(
            warehouse,
            variantIds,
            cancellationToken);
        DateTime today = DateTime.Now.Date;

        var items = pageItems.Select(item =>
        {
            lotByVariant.TryGetValue(item.VariantId, out OperationsLotSummaryRow? lot);
            int onHand = Math.Max(0, lot?.OnHand ?? 0);
            int reserved = Math.Max(0, reservedByVariant.GetValueOrDefault(item.VariantId));
            int available = Math.Max(0, onHand - reserved);
            decimal inventoryValue = Math.Max(0m, lot?.InventoryValue ?? 0m);
            decimal averageCost = onHand > 0
                ? RoundMoney(inventoryValue / onHand)
                : item.CostPrice;

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
                price = item.Price,
                onHand,
                reserved,
                available,
                averageCost,
                latestUnitCost = lot?.LatestUnitCost ?? item.CostPrice,
                inventoryValue,
                lotCount = lot?.LotCount ?? 0,
                oldestStockDays = lot?.OldestReceivedDate.HasValue == true
                    ? Math.Max(0, (today - lot.OldestReceivedDate.Value.Date).Days)
                    : 0,
                warehouseId = warehouse.WarehouseId
            };
        }).ToList();

        return Json(new
        {
            success = true,
            warehouse = new
            {
                warehouseId = warehouse.WarehouseId,
                warehouseCode = warehouse.WarehouseCode,
                warehouseName = warehouse.WarehouseName,
                isPrimary = warehouse.IsPrimary
            },
            page,
            pageSize,
            totalItems,
            totalPages,
            items
        });
    }

    [HttpGet("Replenishment")]
    public async Task<IActionResult> Replenishment(
        int? warehouseId,
        int demandWindowDays = 30,
        int leadTimeDays = 7,
        int safetyDays = 3,
        int fallbackTargetQuantity = 10,
        CancellationToken cancellationToken = default)
    {
        demandWindowDays = Math.Clamp(demandWindowDays, 7, 180);
        leadTimeDays = Math.Clamp(leadTimeDays, 1, 60);
        safetyDays = Math.Clamp(safetyDays, 0, 30);
        fallbackTargetQuantity = Math.Clamp(fallbackTargetQuantity, LowStockThreshold, 5000);

        Warehouses warehouse;
        try
        {
            warehouse = await ResolveWarehouseAsync(warehouseId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(Fail(exception.Message));
        }

        DateTime fromDate = DateTime.Now.Date.AddDays(-demandWindowDays);
        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Where(item => item.IsActive == true && item.Product != null)
            .Select(item => new
            {
                item.VariantId,
                ProductName = item.Product.Name,
                item.Color,
                item.Storage,
                item.Ram,
                ImageUrl = item.ImageUrl ?? item.Product.MainImage,
                BrandName = item.Product.Brand.BrandName,
                CategoryName = item.Product.Category.CategoryName
            })
            .ToListAsync(cancellationToken);

        var variantIds = variants.Select(item => item.VariantId).ToList();
        var stockRows = await _context.InventoryLots
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
        var stockByVariant = stockRows.ToDictionary(item => item.VariantId);

        var onlineDemandRows = await _context.OrderDetails
            .AsNoTracking()
            .Where(item => item.VariantId.HasValue
                && item.Quantity.HasValue
                && item.Quantity.Value > 0
                && item.Order != null
                && item.Order.OrderDate.HasValue
                && item.Order.OrderDate.Value >= fromDate
                && item.Order.Status != null
                && RecognizedDemandStatuses.Contains(item.Order.Status))
            .GroupBy(item => item.VariantId!.Value)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(item => item.Quantity ?? 0)
            })
            .ToListAsync(cancellationToken);
        var onlineDemandByVariant = onlineDemandRows.ToDictionary(item => item.VariantId, item => item.Quantity);

        var distributionDemandRows = await _context.SalesOrderDetails
            .AsNoTracking()
            .Where(item => item.SalesOrder != null
                && item.SalesOrder.FromWarehouseId == warehouse.WarehouseId
                && item.SalesOrder.OrderDate >= fromDate
                && item.SalesOrder.Status == "Completed")
            .GroupBy(item => item.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(item => item.Quantity)
            })
            .ToListAsync(cancellationToken);
        var distributionDemandByVariant = distributionDemandRows.ToDictionary(item => item.VariantId, item => item.Quantity);

        var reservedByVariant = await LoadActiveReservationsAsync(
            warehouse,
            variantIds,
            cancellationToken);

        int planningDays = leadTimeDays + safetyDays;
        var suggestions = variants.Select(item =>
        {
            stockByVariant.TryGetValue(item.VariantId, out var stock);
            int onHand = Math.Max(0, stock?.OnHand ?? 0);
            int reserved = Math.Max(0, reservedByVariant.GetValueOrDefault(item.VariantId));
            int available = Math.Max(0, onHand - reserved);
            int onlineDemand = Math.Max(0, onlineDemandByVariant.GetValueOrDefault(item.VariantId));
            int distributionDemand = Math.Max(0, distributionDemandByVariant.GetValueOrDefault(item.VariantId));
            int totalDemand = onlineDemand + distributionDemand;
            decimal averageDailyDemand = totalDemand / (decimal)demandWindowDays;
            int demandTarget = (int)Math.Ceiling(averageDailyDemand * planningDays);
            int targetQuantity = totalDemand > 0
                ? Math.Max(LowStockThreshold, demandTarget)
                : fallbackTargetQuantity;
            int recommendedQuantity = Math.Max(0, targetQuantity - available);
            decimal coverageDays = averageDailyDemand > 0m
                ? available / averageDailyDemand
                : available > 0 ? 999m : 0m;
            decimal averageCost = onHand > 0
                ? RoundMoney((stock?.InventoryValue ?? 0m) / onHand)
                : 0m;

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
                onHand,
                reserved,
                available,
                onlineDemand,
                distributionDemand,
                totalDemand,
                averageDailyDemand = RoundDecimal(averageDailyDemand, 3),
                coverageDays = coverageDays >= 999m ? (decimal?)null : RoundDecimal(coverageDays, 1),
                targetQuantity,
                recommendedQuantity,
                averageCost,
                estimatedPurchaseValue = RoundMoney(recommendedQuantity * averageCost),
                priority = available == 0 && totalDemand > 0
                    ? "Critical"
                    : coverageDays <= leadTimeDays
                        ? "High"
                        : available <= LowStockThreshold
                            ? "Medium"
                            : "Normal"
            };
        })
        .Where(item => item.recommendedQuantity > 0)
        .OrderBy(item => item.priority == "Critical" ? 0
            : item.priority == "High" ? 1
            : item.priority == "Medium" ? 2 : 3)
        .ThenByDescending(item => item.totalDemand)
        .ThenBy(item => item.available)
        .Take(200)
        .ToList();

        return Json(new
        {
            success = true,
            warehouseId = warehouse.WarehouseId,
            warehouseName = warehouse.WarehouseName,
            demandWindowDays,
            leadTimeDays,
            safetyDays,
            planningDays,
            fallbackTargetQuantity,
            suggestionCount = suggestions.Count,
            totalRecommendedQuantity = suggestions.Sum(item => item.recommendedQuantity),
            estimatedPurchaseValue = suggestions.Sum(item => item.estimatedPurchaseValue),
            suggestions,
            basis = "Nhu cầu đã giao/hoàn thành trong cửa sổ lịch sử + nhu cầu phân phối, trừ tồn khả dụng; mục tiêu bao phủ lead time và safety days."
        });
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

    private static string BuildVariantLabel(
        string? color,
        string? storage,
        string? ram)
    {
        string label = string.Join(" · ", new[] { color, storage, ram }
            .Where(item => !string.IsNullOrWhiteSpace(item)));
        return string.IsNullOrWhiteSpace(label) ? "Biến thể mặc định" : label;
    }

    private static decimal RoundMoney(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundDecimal(decimal value, int decimals)
        => decimal.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static object Fail(string message) => new
    {
        success = false,
        message
    };

    private sealed class OperationsLotSummaryRow
    {
        public int VariantId { get; set; }
        public int OnHand { get; set; }
        public decimal InventoryValue { get; set; }
        public int LotCount { get; set; }
        public DateTime? OldestReceivedDate { get; set; }
        public decimal LatestUnitCost { get; set; }
    }

}
