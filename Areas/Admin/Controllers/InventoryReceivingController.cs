using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;
using TMDT_LT.Services.Inventory.Contracts;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers;

/// <summary>
/// Supplier receiving workspace. Validates supplier invoices, previews landed
/// cost and posts purchase receipt, lots, serials and ledger entries atomically.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory/Receiving")]
public sealed class InventoryReceivingController : Controller
{
    private const int MaxPageSize = 50;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryReceivingController> _logger;

    public InventoryReceivingController(
        ApplicationDbContext context,
        ILogger<InventoryReceivingController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("")]
    [HttpGet("~/Admin/Inventory/CreatePO")]
    public IActionResult Workspace()
    {
        return View("~/Areas/Admin/Views/Inventory/Receiving.cshtml");
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

        var suppliers = await _context.Suppliers
            .AsNoTracking()
            .Where(item => item.IsActive == true && (item.Type == null || item.Type == 1))
            .OrderBy(item => item.SupplierName)
            .Select(item => new
            {
                supplierId = item.SupplierId,
                supplierName = item.SupplierName,
                taxCode = item.TaxCode,
                contactName = item.ContactName,
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
            suppliers,
            categories,
            brands,
            generatedAt = DateTime.Now
        });
    }

    [HttpGet("Products")]
    public async Task<IActionResult> Products(
        string? q,
        int? warehouseId,
        int? categoryId,
        int? brandId,
        string? stockScope,
        int page = 1,
        int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 6, MaxPageSize);
        int selectedWarehouseId;

        try
        {
            selectedWarehouseId = await ResolveWarehouseIdAsync(
                warehouseId,
                cancellationToken);
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
                "available" => query.Where(item => _context.InventoryLots.Any(lot =>
                    lot.WarehouseId == selectedWarehouseId
                    && lot.VariantId == item.VariantId
                    && lot.RemainingQuantity > 0
                    && lot.IsActive
                    && !lot.IsDeleted)),
                "low" => query.Where(item =>
                    (_context.InventoryLots
                        .Where(lot =>
                            lot.WarehouseId == selectedWarehouseId
                            && lot.VariantId == item.VariantId
                            && lot.IsActive
                            && !lot.IsDeleted)
                        .Sum(lot => (int?)lot.RemainingQuantity) ?? 0) > 0
                    && (_context.InventoryLots
                        .Where(lot =>
                            lot.WarehouseId == selectedWarehouseId
                            && lot.VariantId == item.VariantId
                            && lot.IsActive
                            && !lot.IsDeleted)
                        .Sum(lot => (int?)lot.RemainingQuantity) ?? 0) <= 5),
                "out" => query.Where(item => !_context.InventoryLots.Any(lot =>
                    lot.WarehouseId == selectedWarehouseId
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
                CurrentCostPrice = item.CostPrice ?? 0m,
                AggregateStockSnapshot = item.Stock ?? 0,
                ImageUrl = item.ImageUrl ?? item.Product.MainImage,
                item.Product.BrandId,
                BrandName = item.Product.Brand.BrandName,
                item.Product.CategoryId,
                CategoryName = item.Product.Category.CategoryName
            })
            .ToListAsync(cancellationToken);

        var pageIds = pageItems.Select(item => item.VariantId).ToList();
        var warehouseStockByVariant = pageIds.Count == 0
            ? new Dictionary<int, int>()
            : await _context.InventoryLots
                .AsNoTracking()
                .Where(lot =>
                    lot.WarehouseId == selectedWarehouseId
                    && pageIds.Contains(lot.VariantId)
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
                    item => Math.Max(0, item.Quantity),
                    cancellationToken);

        var latestCosts = pageIds.Count == 0
            ? new Dictionary<int, decimal>()
            : (await _context.InventoryLots
                .AsNoTracking()
                .Where(lot =>
                    lot.WarehouseId == selectedWarehouseId
                    && pageIds.Contains(lot.VariantId)
                    && lot.IsActive
                    && !lot.IsDeleted)
                .OrderByDescending(lot => lot.ReceivedDate)
                .ThenByDescending(lot => lot.LotId)
                .Select(lot => new
                {
                    lot.VariantId,
                    lot.UnitCost
                })
                .ToListAsync(cancellationToken))
                .GroupBy(item => item.VariantId)
                .ToDictionary(group => group.Key, group => group.First().UnitCost);

        var items = pageItems
            .Select(item => new
            {
                variantId = item.VariantId,
                productName = item.ProductName,
                color = item.Color,
                storage = item.Storage,
                ram = item.Ram,
                sku = $"SKU-{item.VariantId:D6}",
                price = item.Price,
                stock = warehouseStockByVariant.GetValueOrDefault(item.VariantId),
                aggregateStock = Math.Max(0, item.AggregateStockSnapshot),
                lastImportPrice = latestCosts.GetValueOrDefault(
                    item.VariantId,
                    item.CurrentCostPrice),
                imageUrl = string.IsNullOrWhiteSpace(item.ImageUrl)
                    ? "/images/products/default-product.png"
                    : item.ImageUrl,
                brandId = item.BrandId,
                brandName = item.BrandName,
                categoryId = item.CategoryId,
                categoryName = item.CategoryName,
                warehouseId = selectedWarehouseId
            })
            .ToList();

        return Json(new
        {
            success = true,
            warehouseId = selectedWarehouseId,
            page,
            pageSize,
            totalItems,
            totalPages,
            items
        });
    }

    [HttpGet("Recent")]
    public async Task<IActionResult> RecentReceipts(
        int take = 8,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 20);
        var rawReceipts = await _context.PurchaseOrders
            .AsNoTracking()
            .Include(item => item.Supplier)
            .Include(item => item.Warehouse)
            .OrderByDescending(item => item.Poid)
            .Take(take)
            .Select(item => new
            {
                item.Poid,
                item.InvoiceNumber,
                SupplierName = item.Supplier.SupplierName,
                WarehouseName = item.Warehouse != null
                    ? item.Warehouse.WarehouseName
                    : "Kho chưa xác định",
                item.OrderDate,
                item.Status,
                TotalAmount = item.TotalAmount ?? 0m,
                item.InventoryCapitalizedCost
            })
            .ToListAsync(cancellationToken);

        var receipts = rawReceipts.Select(item => new
        {
            poId = item.Poid,
            poCode = $"PO-{item.Poid:D8}",
            invoiceNumber = item.InvoiceNumber,
            supplierName = item.SupplierName,
            warehouseName = item.WarehouseName,
            orderDate = item.OrderDate,
            status = item.Status,
            totalAmount = item.TotalAmount,
            inventoryCapitalizedCost = item.InventoryCapitalizedCost
        });

        return Json(new { success = true, receipts });
    }

    [HttpPost("Preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewReceipt(
        [FromBody] InventoryReceivingRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateRequestAsync(
            request,
            checkDuplicateInvoice: true,
            cancellationToken);
        if (!validation.Success)
        {
            return BadRequest(Fail(validation.Message));
        }

        PurchaseCostSummary summary;
        try
        {
            summary = CalculateSummary(request!);
        }
        catch (Exception exception)
        {
            return BadRequest(Fail(exception.Message));
        }

        var variantIds = summary.Lines.Select(item => item.VariantId).ToList();
        var names = await _context.ProductVariants
            .AsNoTracking()
            .Where(item => variantIds.Contains(item.VariantId))
            .Select(item => new
            {
                item.VariantId,
                Name = item.Product.Name,
                item.Color,
                item.Storage,
                item.Ram
            })
            .ToDictionaryAsync(item => item.VariantId, cancellationToken);

        return Json(new
        {
            success = true,
            summary.GoodsSubtotal,
            summary.InputVatAmount,
            summary.ShippingFee,
            summary.OtherFee,
            summary.SupplierPayable,
            summary.InventoryCapitalizedCost,
            summary.InputVatDeductible,
            lines = summary.Lines.Select(line => new
            {
                line.VariantId,
                productName = names.GetValueOrDefault(line.VariantId)?.Name
                    ?? $"Biến thể #{line.VariantId}",
                variantLabel = BuildVariantLabel(names.GetValueOrDefault(line.VariantId)),
                line.Quantity,
                line.ImportPrice,
                line.TaxRate,
                line.GoodsAmount,
                line.InputVatAmount,
                line.AllocatedFees,
                line.LandedUnitCost,
                line.CapitalizedLineCost
            }),
            warnings = BuildWarnings(request!, summary)
        });
    }

    [HttpPost("Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReceipt(
        [FromBody] InventoryReceivingRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateRequestAsync(
            request,
            checkDuplicateInvoice: false,
            cancellationToken);
        if (!validation.Success)
        {
            return BadRequest(Fail(validation.Message));
        }

        PurchaseCostSummary summary;
        try
        {
            summary = CalculateSummary(request!);
        }
        catch (Exception exception)
        {
            return BadRequest(Fail(exception.Message));
        }

        int accountId = GetCurrentAccountId();
        if (accountId <= 0)
        {
            return Unauthorized(Fail("Không xác định được tài khoản quản trị đang thao tác."));
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            // Kiểm tra lại trong transaction để chống hai request đồng thời tạo cùng chứng từ.
            string normalizedInvoice = NormalizeInvoiceNumber(request!.InvoiceNumber);
            bool duplicateInvoice = await _context.PurchaseOrders.AnyAsync(
                item =>
                    item.SupplierId == request.SupplierId
                    && item.InvoiceNumber != null
                    && item.InvoiceNumber.ToUpper() == normalizedInvoice,
                cancellationToken);
            if (duplicateInvoice)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(Fail(
                    "Số hóa đơn/chứng từ này đã tồn tại với nhà cung cấp đã chọn."));
            }

            var variantIds = summary.Lines
                .Select(item => item.VariantId)
                .Distinct()
                .ToList();
            var variants = await _context.ProductVariants
                .Where(item =>
                    variantIds.Contains(item.VariantId)
                    && item.IsActive == true)
                .ToDictionaryAsync(item => item.VariantId, cancellationToken);
            if (variants.Count != variantIds.Count)
            {
                throw new InvalidOperationException(
                    "Có biến thể không tồn tại hoặc đã ngừng hoạt động.");
            }

            var now = DateTime.Now;
            DateTime receivedAt = request.ReceivedDate!.Value.Date.Add(now.TimeOfDay);
            var purchaseOrder = new PurchaseOrders
            {
                SupplierId = request.SupplierId,
                AccountId = accountId,
                WarehouseId = request.WarehouseId,
                OrderDate = request.InvoiceDate,
                InvoiceDate = request.InvoiceDate,
                InvoiceNumber = request.InvoiceNumber!.Trim(),
                TotalAmount = summary.SupplierPayable,
                GoodsSubtotal = summary.GoodsSubtotal,
                InputVatAmount = summary.InputVatAmount,
                InboundShippingFee = summary.ShippingFee,
                OtherCost = summary.OtherFee,
                InventoryCapitalizedCost = summary.InventoryCapitalizedCost,
                InputVatDeductible = summary.InputVatDeductible,
                Status = "Hoàn thành",
                Note = BuildReceiptNote(request)
            };

            _context.PurchaseOrders.Add(purchaseOrder);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var line in summary.Lines)
            {
                ProductVariants variant = variants[line.VariantId];

                _context.PurchaseOrderDetails.Add(new PurchaseOrderDetails
                {
                    Poid = purchaseOrder.Poid,
                    VariantId = line.VariantId,
                    Quantity = line.Quantity,
                    ImportPrice = line.ImportPrice,
                    TaxRate = line.TaxRate,
                    InputVatAmount = line.InputVatAmount,
                    AllocatedInboundCost = line.AllocatedFees,
                    LandedUnitCost = line.LandedUnitCost,
                    CapitalizedLineCost = line.CapitalizedLineCost
                });

                var lot = new InventoryLots
                {
                    Poid = purchaseOrder.Poid,
                    VariantId = line.VariantId,
                    SupplierId = request.SupplierId,
                    WarehouseId = request.WarehouseId,
                    ReceivedQuantity = line.Quantity,
                    RemainingQuantity = line.Quantity,
                    UnitCost = line.LandedUnitCost,
                    ReceivedDate = receivedAt,
                    IsActive = true,
                    IsDeleted = false
                };
                _context.InventoryLots.Add(lot);
                await _context.SaveChangesAsync(cancellationToken);

                for (int index = 1; index <= line.Quantity; index++)
                {
                    _context.ProductSerials.Add(new ProductSerials
                    {
                        VariantId = line.VariantId,
                        LotId = lot.LotId,
                        SerialNumber =
                            $"STK-{request.WarehouseId:D2}-{line.VariantId:D6}-{lot.LotId:D8}-{index:D5}",
                        Status = "InStock",
                        CreatedDate = now
                    });
                }

                int stockBefore = Math.Max(0, variant.Stock ?? 0);
                variant.Stock = await CalculateAggregateVariantStockAsync(
                    line.VariantId,
                    cancellationToken);
                variant.CostPrice = line.LandedUnitCost;
                variant.UpdatedDate = now;

                _context.InventoryTransactions.Add(new InventoryTransactions
                {
                    VariantId = line.VariantId,
                    TransactionType = "IN_PURCHASE",
                    Quantity = line.Quantity,
                    ReferenceId = purchaseOrder.Poid,
                    TransactionDate = now,
                    AccountId = accountId,
                    WarehouseId = request.WarehouseId,
                    LotId = lot.LotId,
                    UnitCostSnapshot = line.LandedUnitCost,
                    TotalCostSnapshot = line.CapitalizedLineCost,
                    Note =
                        $"Nhận hàng PO #{purchaseOrder.Poid}; chứng từ {request.InvoiceNumber}; "
                        + $"lô #{lot.LotId}; tồn tổng {stockBefore}->{variant.Stock}."
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                poId = purchaseOrder.Poid,
                poCode = $"PO-{purchaseOrder.Poid:D8}",
                invoiceNumber = purchaseOrder.InvoiceNumber,
                summary.SupplierPayable,
                summary.InventoryCapitalizedCost,
                totalQuantity = summary.Lines.Sum(item => item.Quantity),
                message = "Đã ghi nhận phiếu nhập và cập nhật tồn kho theo lô."
            });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                exception,
                "Inventory receiving concurrency conflict for invoice {InvoiceNumber}.",
                request?.InvoiceNumber);
            return Conflict(Fail(
                "Dữ liệu sản phẩm vừa được thay đổi bởi giao dịch khác. Hãy tải lại và thử lại."));
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                exception,
                "inventory inventory receipt failed for invoice {InvoiceNumber}.",
                request?.InvoiceNumber);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Fail("Không thể ghi nhận phiếu nhập: " + exception.Message));
        }
    }

    private async Task<InventoryReceivingValidationResult> ValidateRequestAsync(
        InventoryReceivingRequest? request,
        bool checkDuplicateInvoice,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return InventoryReceivingValidationResult.Invalid("Dữ liệu phiếu nhập không hợp lệ.");
        }

        if (request.SupplierId <= 0 || request.WarehouseId <= 0)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Nhà cung cấp và kho nhận là thông tin bắt buộc.");
        }

        if (string.IsNullOrWhiteSpace(request.InvoiceNumber)
            || request.InvoiceNumber.Trim().Length < 3
            || request.InvoiceNumber.Trim().Length > 100)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Số hóa đơn/chứng từ nhà cung cấp phải từ 3 đến 100 ký tự.");
        }

        DateTime today = DateTime.Today;
        if (!request.InvoiceDate.HasValue || request.InvoiceDate.Value.Date > today)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Ngày hóa đơn/chứng từ không hợp lệ hoặc nằm trong tương lai.");
        }

        if (!request.ReceivedDate.HasValue || request.ReceivedDate.Value.Date > today)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Ngày nhận hàng không hợp lệ hoặc nằm trong tương lai.");
        }

        if (request.ReceivedDate.Value.Date < request.InvoiceDate.Value.Date.AddDays(-30))
        {
            return InventoryReceivingValidationResult.Invalid(
                "Ngày nhận hàng cách ngày chứng từ quá xa về phía trước. Hãy kiểm tra lại dữ liệu.");
        }

        if (request.ShippingFee < 0m || request.OtherFee < 0m)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Phí vận chuyển và chi phí khác không được âm.");
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Phiếu nhập phải có ít nhất một mặt hàng.");
        }

        if (request.Items.Count > 200)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Một phiếu nhập không được vượt quá 200 dòng hàng.");
        }

        if (request.Items.Any(item =>
                item.VariantId <= 0
                || item.Quantity <= 0
                || item.Quantity > 10000
                || item.ImportPrice <= 0m
                || item.ImportPrice > 1_000_000_000m
                || item.TaxRate < 0m
                || item.TaxRate > 100m))
        {
            return InventoryReceivingValidationResult.Invalid(
                "Có dòng hàng chứa biến thể, số lượng, giá nhập hoặc thuế suất không hợp lệ.");
        }

        var grouped = request.Items.GroupBy(item => item.VariantId).ToList();
        if (grouped.Any(group => group.Count() > 1))
        {
            return InventoryReceivingValidationResult.Invalid(
                "Mỗi biến thể chỉ được xuất hiện một lần trong phiếu nhập.");
        }

        bool supplierExists = await _context.Suppliers.AnyAsync(
            item =>
                item.SupplierId == request.SupplierId
                && item.IsActive == true
                && (item.Type == null || item.Type == 1),
            cancellationToken);
        if (!supplierExists)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Nhà cung cấp không tồn tại, không đúng loại hoặc đã ngừng hoạt động.");
        }

        bool warehouseExists = await _context.Warehouses.AnyAsync(
            item => item.WarehouseId == request.WarehouseId && item.IsActive,
            cancellationToken);
        if (!warehouseExists)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Kho nhận không tồn tại hoặc đã ngừng hoạt động.");
        }

        var variantIds = request.Items.Select(item => item.VariantId).Distinct().ToList();
        int activeVariantCount = await _context.ProductVariants.CountAsync(
            item => variantIds.Contains(item.VariantId) && item.IsActive == true,
            cancellationToken);
        if (activeVariantCount != variantIds.Count)
        {
            return InventoryReceivingValidationResult.Invalid(
                "Có biến thể không tồn tại hoặc đã ngừng hoạt động.");
        }

        if (checkDuplicateInvoice)
        {
            string normalizedInvoice = NormalizeInvoiceNumber(request.InvoiceNumber);
            bool duplicateInvoice = await _context.PurchaseOrders
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.SupplierId == request.SupplierId
                        && item.InvoiceNumber != null
                        && item.InvoiceNumber.ToUpper() == normalizedInvoice,
                    cancellationToken);
            if (duplicateInvoice)
            {
                return InventoryReceivingValidationResult.Invalid(
                    "Số hóa đơn/chứng từ này đã tồn tại với nhà cung cấp đã chọn.");
            }
        }

        return InventoryReceivingValidationResult.Valid();
    }

    private static PurchaseCostSummary CalculateSummary(InventoryReceivingRequest request)
    {
        return InventoryFinancialCalculator.CalculatePurchaseCosts(
            request.Items.Select(item => new PurchaseCostInput(
                item.VariantId,
                item.Quantity,
                item.ImportPrice,
                item.TaxRate)).ToList(),
            request.ShippingFee,
            request.OtherFee,
            request.InputVatDeductible);
    }

    private async Task<int> ResolveWarehouseIdAsync(
        int? requestedWarehouseId,
        CancellationToken cancellationToken)
    {
        if (requestedWarehouseId.HasValue && requestedWarehouseId.Value > 0)
        {
            bool exists = await _context.Warehouses.AnyAsync(
                item =>
                    item.WarehouseId == requestedWarehouseId.Value
                    && item.IsActive,
                cancellationToken);
            if (!exists)
            {
                throw new InvalidOperationException(
                    "Kho được chọn không tồn tại hoặc đã ngừng hoạt động.");
            }
            return requestedWarehouseId.Value;
        }

        int warehouseId = await _context.Warehouses
            .Where(item => item.IsActive)
            .OrderByDescending(item => item.IsPrimary)
            .ThenBy(item => item.WarehouseId)
            .Select(item => item.WarehouseId)
            .FirstOrDefaultAsync(cancellationToken);
        if (warehouseId <= 0)
        {
            throw new InvalidOperationException("Hệ thống chưa có kho hoạt động.");
        }
        return warehouseId;
    }

    private async Task<int> CalculateAggregateVariantStockAsync(
        int variantId,
        CancellationToken cancellationToken)
    {
        return await _context.InventoryLots
            .Where(lot =>
                lot.VariantId == variantId
                && lot.IsActive
                && !lot.IsDeleted)
            .SumAsync(lot => (int?)lot.RemainingQuantity, cancellationToken)
            ?? 0;
    }

    private int GetCurrentAccountId()
    {
        string? value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out int accountId) ? accountId : 0;
    }

    private static string NormalizeInvoiceNumber(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string BuildReceiptNote(InventoryReceivingRequest request)
    {
        return string.Join(
            " | ",
            new[]
            {
                "Nhận hàng nhà cung cấp",
                request.InputVatDeductible
                    ? "VAT đầu vào khấu trừ, không vốn hóa"
                    : "VAT đầu vào không khấu trừ, đã vốn hóa",
                string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
            }.Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    private static string BuildVariantLabel(dynamic? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        return string.Join(
            " / ",
            new[] { (string?)item.Color, (string?)item.Storage, (string?)item.Ram }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static IReadOnlyList<string> BuildWarnings(
        InventoryReceivingRequest request,
        PurchaseCostSummary summary)
    {
        var warnings = new List<string>();
        if (summary.ShippingFee + summary.OtherFee > summary.GoodsSubtotal * 0.2m)
        {
            warnings.Add(
                "Tổng chi phí nhập vượt 20% tiền hàng. Hãy kiểm tra lại trước khi ghi nhận.");
        }
        if (!request.InputVatDeductible && summary.InputVatAmount > 0m)
        {
            warnings.Add(
                "VAT đầu vào không khấu trừ đang được vốn hóa vào giá trị tồn kho.");
        }
        if (request.ReceivedDate!.Value.Date < DateTime.Today.AddDays(-7))
        {
            warnings.Add(
                "Ngày nhận hàng đã quá 7 ngày. Cần bảo đảm tồn kho thực tế khớp với ngày ghi nhận.");
        }
        return warnings;
    }

    private static object Fail(string message) => new
    {
        success = false,
        message
    };
}

internal sealed record InventoryReceivingValidationResult(bool Success, string Message)
{
    public static InventoryReceivingValidationResult Valid() => new(true, string.Empty);
    public static InventoryReceivingValidationResult Invalid(string message) => new(false, message);
}

