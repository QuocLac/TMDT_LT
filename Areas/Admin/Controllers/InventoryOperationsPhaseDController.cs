using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers;

/// <summary>
/// Phase D3 - Trung tâm vận hành kho.
/// Hoàn thiện bổ sung hàng, điều chuyển đa SKU, xuất phân phối an toàn,
/// cảnh báo vận hành và đối soát tồn tổng dựa trên sổ lô.
/// Không thay đổi schema database.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory/PhaseD/Operations")]
public sealed class InventoryOperationsPhaseDController : Controller
{
    private const int DefaultPageSize = 12;
    private const int MaxPageSize = 50;
    private const int LowStockThreshold = 5;
    private const int AgedStockDays = 90;
    private const int MaxTransferLines = 100;

    private static readonly string[] RecognizedDemandStatuses =
    {
        "Completed",
        "Delivered",
        "Đã giao",
        "Đã hoàn thành",
        "Hoàn thành"
    };

    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryOperationsPhaseDController> _logger;
    private readonly ILogger<InventoryWarehousePhaseBController> _phaseBLogger;

    public InventoryOperationsPhaseDController(
        ApplicationDbContext context,
        ILogger<InventoryOperationsPhaseDController> logger,
        ILogger<InventoryWarehousePhaseBController> phaseBLogger)
    {
        _context = context;
        _logger = logger;
        _phaseBLogger = phaseBLogger;
    }

    [HttpGet("~/Admin/Inventory/Operations", Order = -500)]
    public IActionResult Workspace()
    {
        return View("~/Areas/Admin/Views/Inventory/Operations.cshtml");
    }

    [HttpGet("~/Admin/Inventory/CreateSO", Order = -500)]
    public IActionResult DistributionWorkspace()
    {
        return View("~/Areas/Admin/Views/Inventory/CreateSO.cshtml");
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

    [HttpPost("Transfers/Preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewTransfer(
        [FromBody] InventoryTransferBatchRequest? request,
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

    [HttpPost("Transfers/Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitTransfer(
        [FromBody] InventoryTransferBatchRequest? request,
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

        bool hasOpenCount = await _context.Set<InventoryCountSessions>()
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
            _logger.LogError(exception, "Phase D3 warehouse transfer failed.");
            return BadRequest(Fail("Không thể điều chuyển kho: " + exception.Message));
        }
    }

    [HttpGet("Transfers/Recent")]
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

    [HttpPost("Distribution/Preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewDistribution(
        [FromBody] InventoryWarehouseSoRequest? request,
        CancellationToken cancellationToken)
    {
        var guard = await ValidateDistributionAvailabilityAsync(request, cancellationToken);
        if (!guard.Success)
        {
            return BadRequest(Fail(guard.Message));
        }

        var phaseB = CreatePhaseBController();
        return await phaseB.EstimateSO(request, cancellationToken);
    }

    [HttpPost("Distribution/Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitDistribution(
        [FromBody] InventoryWarehouseSoRequest? request,
        CancellationToken cancellationToken)
    {
        var guard = await ValidateDistributionAvailabilityAsync(request, cancellationToken);
        if (!guard.Success)
        {
            return BadRequest(Fail(guard.Message));
        }

        var phaseB = CreatePhaseBController();
        return await phaseB.SubmitSO(request, cancellationToken);
    }

    [HttpGet("Distribution/Recent")]
    public async Task<IActionResult> RecentDistributions(
        int take = 12,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 50);
        var distributions = await _context.SalesOrders
            .AsNoTracking()
            .OrderByDescending(item => item.OrderDate)
            .ThenByDescending(item => item.SOId)
            .Take(take)
            .Select(item => new
            {
                soId = item.SOId,
                soCode = item.SOCode,
                storeId = item.StoreId,
                storeName = item.Store != null ? item.Store.StoreName : $"Cửa hàng #{item.StoreId}",
                warehouseId = item.FromWarehouseId,
                warehouseName = item.FromWarehouse != null
                    ? item.FromWarehouse.WarehouseName
                    : $"Kho #{item.FromWarehouseId}",
                orderDate = item.OrderDate,
                status = item.Status,
                invoiceNumber = item.InvoiceNumber,
                totalAmount = item.TotalAmount,
                cogs = item.COGSTotal,
                profit = item.ProfitTotal,
                lineCount = item.SalesOrderDetails.Select(line => line.VariantId).Distinct().Count(),
                totalQuantity = item.SalesOrderDetails.Sum(line => line.Quantity)
            })
            .ToListAsync(cancellationToken);

        return Json(new { success = true, distributions });
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

        int openCountSessions = await _context.Set<InventoryCountSessions>()
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
            _logger.LogError(exception, "Phase D3 variant snapshot repair failed.");
            return StatusCode(500, Fail("Không thể đồng bộ snapshot tồn: " + exception.Message));
        }
    }

    private InventoryWarehousePhaseBController CreatePhaseBController()
    {
        return new InventoryWarehousePhaseBController(_context, _phaseBLogger)
        {
            ControllerContext = ControllerContext
        };
    }

    private async Task<OperationValidationResult> ValidateDistributionAvailabilityAsync(
        InventoryWarehouseSoRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null || request.Items.Count == 0)
        {
            return OperationValidationResult.Fail("Phiếu phân phối chưa có mặt hàng.");
        }

        Warehouses warehouse;
        try
        {
            warehouse = await ResolveWarehouseAsync(request.FromWarehouseId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return OperationValidationResult.Fail(exception.Message);
        }

        var normalizedItems = request.Items
            .Where(item => item.VariantId > 0 && item.Quantity > 0)
            .GroupBy(item => item.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(item => item.Quantity)
            })
            .ToList();
        if (normalizedItems.Count == 0)
        {
            return OperationValidationResult.Fail("Không có dòng phân phối hợp lệ.");
        }

        var ids = normalizedItems.Select(item => item.VariantId).ToList();
        bool hasOpenCount = await _context.Set<InventoryCountSessions>()
            .AsNoTracking()
            .AnyAsync(session => session.WarehouseId == warehouse.WarehouseId
                && (session.Status == InventoryCountSessionStatuses.Counting
                    || session.Status == InventoryCountSessionStatuses.PendingApproval)
                && session.Lines.Any(line => ids.Contains(line.VariantId)),
                cancellationToken);
        if (hasOpenCount)
        {
            return OperationValidationResult.Fail(
                "Có SKU đang nằm trong phiên kiểm kê mở tại kho xuất.");
        }

        var onHandRows = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot => lot.WarehouseId == warehouse.WarehouseId
                && ids.Contains(lot.VariantId)
                && lot.IsActive
                && !lot.IsDeleted)
            .GroupBy(lot => lot.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(lot => lot.RemainingQuantity)
            })
            .ToDictionaryAsync(item => item.VariantId, item => item.Quantity, cancellationToken);
        var reserved = await LoadActiveReservationsAsync(warehouse, ids, cancellationToken);

        foreach (var line in normalizedItems)
        {
            int available = Math.Max(0,
                onHandRows.GetValueOrDefault(line.VariantId)
                - reserved.GetValueOrDefault(line.VariantId));
            if (available < line.Quantity)
            {
                return OperationValidationResult.Fail(
                    $"SKU #{line.VariantId} chỉ còn {available} khả dụng sau reservation, cần {line.Quantity}.");
            }
        }

        return OperationValidationResult.Ok();
    }

    private async Task<TransferValidationResult> ValidateTransferAsync(
        InventoryTransferBatchRequest? request,
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
            .Select(group => new InventoryTransferBatchItemRequest
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

    private sealed record OperationValidationResult(bool Success, string Message)
    {
        public static OperationValidationResult Ok() => new(true, string.Empty);
        public static OperationValidationResult Fail(string message) => new(false, message);
    }

    private sealed record TransferValidationResult(
        bool Success,
        string Message,
        Warehouses? SourceWarehouse,
        Warehouses? TargetWarehouse,
        List<InventoryTransferBatchItemRequest>? Items)
    {
        public static TransferValidationResult Ok(
            Warehouses source,
            Warehouses target,
            List<InventoryTransferBatchItemRequest> items)
            => new(true, string.Empty, source, target, items);

        public static TransferValidationResult Fail(string message)
            => new(false, message, null, null, null);
    }
}

public sealed class InventoryTransferBatchRequest
{
    public int SourceWarehouseId { get; set; }
    public int TargetWarehouseId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<InventoryTransferBatchItemRequest> Items { get; set; } = new();
}

public sealed class InventoryTransferBatchItemRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; }
}

public sealed class InventorySnapshotRepairRequest
{
    public string Reason { get; set; } = string.Empty;
}
