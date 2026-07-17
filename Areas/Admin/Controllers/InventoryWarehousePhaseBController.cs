using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers;

/// <summary>
/// Phase B: đưa WarehouseId vào lô, FIFO theo kho, luân chuyển kho,
/// snapshot tài chính PO/SO và báo cáo lợi nhuận thực nhận gần nhất.
/// Route Order thấp hơn Phase A để thay thế đúng các action nghiệp vụ.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory")]
[Route("Admin/Inventory/PhaseB")]
public sealed class InventoryWarehousePhaseBController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryWarehousePhaseBController> _logger;

    public InventoryWarehousePhaseBController(
        ApplicationDbContext context,
        ILogger<InventoryWarehousePhaseBController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("Warehouses", Order = -200)]
    public async Task<IActionResult> Warehouses(
        CancellationToken cancellationToken)
    {
        var warehouses = await _context.Warehouses
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderByDescending(item => item.IsPrimary)
            .ThenBy(item => item.WarehouseId)
            .Select(item => new
            {
                warehouseId = item.WarehouseId,
                warehouseCode = item.WarehouseCode,
                warehouseName = item.WarehouseName,
                item.Address,
                item.IsPrimary
            })
            .ToListAsync(cancellationToken);

        return Json(warehouses);
    }

    [HttpGet("GetAllProductsWithStock", Order = -200)]
    public async Task<IActionResult> GetAllProductsWithStock(
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        int selectedWarehouseId = await ResolveWarehouseIdAsync(
            warehouseId,
            cancellationToken);

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(item => item.Product)
                .ThenInclude(product => product!.Brand)
            .Include(item => item.Product)
                .ThenInclude(product => product!.Category)
            .Where(item => item.IsActive == true && item.Product != null)
            .OrderBy(item => item.Product!.Name)
            .ThenBy(item => item.VariantId)
            .ToListAsync(cancellationToken);

        var variantIds = variants.Select(item => item.VariantId).ToList();
        var warehouseLots = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot =>
                lot.WarehouseId == selectedWarehouseId
                && variantIds.Contains(lot.VariantId)
                && lot.IsActive
                && !lot.IsDeleted)
            .ToListAsync(cancellationToken);

        var result = variants.Select(variant =>
        {
            var lots = warehouseLots
                .Where(lot => lot.VariantId == variant.VariantId)
                .ToList();
            int warehouseStock = lots.Sum(lot =>
                Math.Max(0, lot.RemainingQuantity));
            decimal latestCost = lots
                .OrderByDescending(lot => lot.ReceivedDate)
                .ThenByDescending(lot => lot.LotId)
                .Select(lot => (decimal?)lot.UnitCost)
                .FirstOrDefault()
                ?? variant.CostPrice
                ?? 0m;

            return new
            {
                variantId = variant.VariantId,
                productName = variant.Product!.Name,
                color = variant.Color,
                storage = variant.Storage,
                ram = variant.Ram,
                price = variant.Price ?? 0m,
                stock = warehouseStock,
                aggregateStock = Math.Max(0, variant.Stock ?? 0),
                lastImportPrice = latestCost,
                imageUrl = variant.ImageUrl
                    ?? variant.Product.MainImage
                    ?? "/images/products/default-product.png",
                brandId = variant.Product.BrandId,
                categoryId = variant.Product.CategoryId,
                brandName = variant.Product.Brand?.BrandName ?? string.Empty,
                categoryName = variant.Product.Category?.CategoryName ?? string.Empty,
                warehouseId = selectedWarehouseId
            };
        });

        return Json(result);
    }

    [HttpGet("SearchVariants", Order = -200)]
    public async Task<IActionResult> SearchVariants(
        string q,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        q = (q ?? string.Empty).Trim();
        if (q.Length < 2)
        {
            return Json(Array.Empty<object>());
        }

        int selectedWarehouseId = await ResolveWarehouseIdAsync(
            warehouseId,
            cancellationToken);

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(item => item.Product)
            .Where(item =>
                item.IsActive == true
                && item.Product != null
                && (item.Product.Name.Contains(q)
                    || (item.Color != null && item.Color.Contains(q))
                    || (item.Storage != null && item.Storage.Contains(q))
                    || (item.Ram != null && item.Ram.Contains(q))))
            .Take(10)
            .ToListAsync(cancellationToken);

        var ids = variants.Select(item => item.VariantId).ToList();
        var allActiveLots = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot =>
                ids.Contains(lot.VariantId)
                && lot.IsActive
                && !lot.IsDeleted)
            .ToListAsync(cancellationToken);
        var selectedWarehouseLots = allActiveLots
            .Where(lot => lot.WarehouseId == selectedWarehouseId)
            .ToList();
        var aggregateStockByVariant = allActiveLots
            .GroupBy(lot => lot.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(lot => Math.Max(0, lot.RemainingQuantity)));

        return Json(variants.Select(variant =>
        {
            var variantLots = selectedWarehouseLots
                .Where(lot => lot.VariantId == variant.VariantId)
                .ToList();
            int stock = variantLots.Sum(lot =>
                Math.Max(0, lot.RemainingQuantity));
            decimal latestCost = variantLots
                .OrderByDescending(lot => lot.ReceivedDate)
                .ThenByDescending(lot => lot.LotId)
                .Select(lot => (decimal?)lot.UnitCost)
                .FirstOrDefault()
                ?? variant.CostPrice
                ?? 0m;
            int aggregateLotStock = aggregateStockByVariant
                .GetValueOrDefault(variant.VariantId);

            return new
            {
                variantId = variant.VariantId,
                productName = variant.Product!.Name,
                variant.Color,
                storage = string.Join(
                    " ",
                    new[] { variant.Storage, variant.Ram }
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                stock,
                aggregateStock = Math.Max(0, variant.Stock ?? 0),
                stockMismatch = Math.Max(0, variant.Stock ?? 0)
                    != aggregateLotStock,
                lastImportPrice = latestCost,
                warehouseId = selectedWarehouseId
            };
        }));
    }

    [HttpPost("SubmitPO", Order = -200)]
    public async Task<IActionResult> SubmitPO(
        [FromBody] InventoryWarehousePoRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null || request.Items.Count == 0)
        {
            return Json(Fail("Phiếu nhập chưa có mặt hàng hợp lệ."));
        }

        if (request.SupplierId <= 0)
        {
            return Json(Fail("Nhà cung cấp không hợp lệ."));
        }

        int warehouseId;
        try
        {
            warehouseId = await ResolveWarehouseIdAsync(
                request.WarehouseId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return Json(Fail(exception.Message));
        }

        var normalizedItems = request.Items
            .Where(item => item.Quantity > 0 && item.ImportPrice > 0m)
            .GroupBy(item => item.VariantId)
            .Select(group =>
            {
                var first = group.First();
                return new
                {
                    first.VariantId,
                    Quantity = group.Sum(item => item.Quantity),
                    first.ImportPrice,
                    first.TaxRate,
                    Inconsistent = group.Any(item =>
                        item.ImportPrice != first.ImportPrice
                        || item.TaxRate != first.TaxRate)
                };
            })
            .ToList();

        if (normalizedItems.Count == 0
            || normalizedItems.Any(item => item.Inconsistent))
        {
            return Json(Fail(
                "Dòng nhập không hợp lệ hoặc một biến thể có nhiều giá/thuế suất khác nhau."));
        }

        bool supplierExists = await _context.Suppliers.AnyAsync(
            item =>
                item.SupplierId == request.SupplierId
                && item.IsActive == true,
            cancellationToken);
        if (!supplierExists)
        {
            return Json(Fail("Nhà cung cấp không tồn tại hoặc đã ngừng hoạt động."));
        }

        var variantIds = normalizedItems
            .Select(item => item.VariantId)
            .Distinct()
            .ToList();
        var variants = await _context.ProductVariants
            .Where(item => variantIds.Contains(item.VariantId))
            .ToDictionaryAsync(item => item.VariantId, cancellationToken);
        if (variants.Count != variantIds.Count)
        {
            return Json(Fail("Có biến thể không tồn tại trong hệ thống."));
        }

        PurchaseCostSummary financial;
        try
        {
            financial = InventoryFinancialCalculator.CalculatePurchaseCosts(
                normalizedItems.Select(item => new PurchaseCostInput(
                    item.VariantId,
                    item.Quantity,
                    item.ImportPrice,
                    item.TaxRate)).ToList(),
                request.ShippingFee,
                request.OtherFee,
                request.InputVatDeductible);
        }
        catch (Exception exception)
        {
            return Json(Fail(exception.Message));
        }

        var now = DateTime.Now;
        var receivedDate = request.ReceivedDate?.Date.Add(now.TimeOfDay) ?? now;
        if (receivedDate > now.AddMinutes(5))
        {
            return Json(Fail("Ngày nhận kho không được nằm trong tương lai."));
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var purchaseOrder = new PurchaseOrders
            {
                SupplierId = request.SupplierId,
                AccountId = GetCurrentAccountId(),
                WarehouseId = warehouseId,
                OrderDate = request.InvoiceDate ?? now,
                InvoiceDate = request.InvoiceDate ?? now,
                InvoiceNumber = string.IsNullOrWhiteSpace(request.InvoiceNumber)
                    ? null
                    : request.InvoiceNumber.Trim(),
                TotalAmount = financial.SupplierPayable,
                GoodsSubtotal = financial.GoodsSubtotal,
                InputVatAmount = financial.InputVatAmount,
                InboundShippingFee = financial.ShippingFee,
                OtherCost = financial.OtherFee,
                InventoryCapitalizedCost = financial.InventoryCapitalizedCost,
                InputVatDeductible = financial.InputVatDeductible,
                Status = "Hoàn thành",
                Note = string.IsNullOrWhiteSpace(request.Note)
                    ? null
                    : request.Note.Trim()
            };
            _context.PurchaseOrders.Add(purchaseOrder);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var line in financial.Lines)
            {
                var variant = variants[line.VariantId];
                _context.PurchaseOrderDetails.Add(
                    new PurchaseOrderDetails
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
                    WarehouseId = warehouseId,
                    ReceivedQuantity = line.Quantity,
                    RemainingQuantity = line.Quantity,
                    UnitCost = line.LandedUnitCost,
                    ReceivedDate = receivedDate,
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
                            $"IMEI-{line.VariantId}-{lot.LotId}-{index:0000}",
                        Status = "InStock",
                        CreatedDate = now
                    });
                }

                int before = Math.Max(0, variant.Stock ?? 0);
                variant.Stock = await CalculateAggregateVariantStockAsync(
                    variant.VariantId,
                    cancellationToken);
                variant.CostPrice = line.LandedUnitCost;

                _context.InventoryTransactions.Add(
                    new InventoryTransactions
                    {
                        VariantId = line.VariantId,
                        TransactionType = "IN_PURCHASE",
                        Quantity = line.Quantity,
                        ReferenceId = purchaseOrder.Poid,
                        TransactionDate = now,
                        AccountId = GetCurrentAccountId(),
                        WarehouseId = warehouseId,
                        LotId = lot.LotId,
                        UnitCostSnapshot = line.LandedUnitCost,
                        TotalCostSnapshot = line.CapitalizedLineCost,
                        Note =
                            $"Nhập PO #{purchaseOrder.Poid} vào kho #{warehouseId}; "
                            + $"lô #{lot.LotId}; tồn tổng {before}->{variant.Stock}."
                    });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                poId = purchaseOrder.Poid,
                poCode = $"PO-{purchaseOrder.Poid:D8}",
                warehouseId,
                supplierPayable = financial.SupplierPayable,
                goodsSubtotal = financial.GoodsSubtotal,
                inputVatAmount = financial.InputVatAmount,
                inventoryCapitalizedCost = financial.InventoryCapitalizedCost,
                inputVatDeductible = financial.InputVatDeductible
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Phase B SubmitPO failed.");
            return Json(Fail("Không thể ghi nhận phiếu nhập: " + exception.Message));
        }
    }

    [HttpPost("EstimateSO", Order = -200)]
    public async Task<IActionResult> EstimateSO(
        [FromBody] InventoryWarehouseSoRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSalesRequest(request);
        if (validation != null)
        {
            return Json(validation);
        }

        try
        {
            var plan = await BuildSalesPlanAsync(
                request!,
                tracking: false,
                cancellationToken);
            return Json(new
            {
                success = true,
                warehouseId = request!.FromWarehouseId,
                grossSalesBeforeTax = plan.Financial.GrossSalesBeforeTax,
                discountAmount = plan.Financial.DiscountAmount,
                netRevenue = plan.Financial.NetSalesBeforeTax,
                taxAmount = plan.Financial.OutputVatAmount,
                invoiceTotal = plan.Financial.InvoiceTotal,
                cogs = plan.Financial.Cogs,
                shippingCost = plan.Financial.ShippingCost,
                realizedProfit = plan.Financial.RealizedAccountingProfit,
                marginPercent = plan.Financial.MarginPercent,
                accountingBasis =
                    "Doanh thu thuần chưa VAT - FIFO COGS của kho đã chọn - chi phí vận chuyển."
            });
        }
        catch (Exception exception)
        {
            return Json(Fail(exception.Message));
        }
    }

    [HttpPost("SubmitSO", Order = -200)]
    public async Task<IActionResult> SubmitSO(
        [FromBody] InventoryWarehouseSoRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSalesRequest(request);
        if (validation != null)
        {
            return Json(validation);
        }

        bool storeExists = await _context.Stores.AnyAsync(
            item => item.StoreId == request!.StoreId && item.IsActive,
            cancellationToken);
        if (!storeExists)
        {
            return Json(Fail("Cửa hàng nhận phân phối không hợp lệ."));
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var plan = await BuildSalesPlanAsync(
                request!,
                tracking: true,
                cancellationToken);
            var now = DateTime.Now;
            string code =
                $"SO-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            var salesOrder = new SalesOrders
            {
                SOCode = code,
                StoreId = request!.StoreId,
                FromWarehouseId = request.FromWarehouseId,
                OrderDate = now,
                Status = "Completed",
                InvoiceNumber = string.IsNullOrWhiteSpace(request.InvoiceNumber)
                    ? null
                    : request.InvoiceNumber.Trim(),
                DiscountPercent = Math.Clamp(request.DiscountPercent, 0m, 100m),
                DiscountAmount = plan.Financial.DiscountAmount,
                TaxAmount = plan.Financial.OutputVatAmount,
                ShippingCost = Math.Max(0m, request.ShippingCost),
                TotalAmount = plan.Financial.InvoiceTotal,
                COGSTotal = plan.Financial.Cogs,
                ProfitTotal = plan.Financial.RealizedAccountingProfit,
                AccountId = GetCurrentAccountId(),
                Notes = BuildSalesOrderNote(request.Notes)
            };
            _context.SalesOrders.Add(salesOrder);
            await _context.SaveChangesAsync(cancellationToken);

            var createdDetails = new List<SalesOrderDetails>();
            decimal remainingShipping = plan.Financial.ShippingCost;
            decimal totalNetRevenue = plan.Financial.NetSalesBeforeTax;
            int allocationIndex = 0;

            foreach (var allocation in plan.Allocations)
            {
                allocationIndex++;
                var line = plan.Financial.Lines.Single(item =>
                    item.VariantId == allocation.VariantId);
                decimal allocationNetRevenue = allocation.IsLastForVariant
                    ? InventoryFinancialCalculator.RoundMoney(
                        line.NetBeforeTax - allocation.NetRevenueAlreadyAllocated)
                    : InventoryFinancialCalculator.RoundMoney(
                        line.NetBeforeTax * allocation.Quantity / line.Quantity);

                decimal shippingShare;
                if (allocationIndex == plan.Allocations.Count)
                {
                    shippingShare = remainingShipping;
                }
                else if (totalNetRevenue <= 0m)
                {
                    shippingShare = 0m;
                }
                else
                {
                    shippingShare = InventoryFinancialCalculator.RoundMoney(
                        plan.Financial.ShippingCost
                        * allocationNetRevenue
                        / totalNetRevenue);
                    remainingShipping -= shippingShare;
                }

                int affected = await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                      UPDATE InventoryLots
                      SET RemainingQuantity = RemainingQuantity - {allocation.Quantity}
                      WHERE LotId = {allocation.Lot.LotId}
                        AND WarehouseId = {request.FromWarehouseId}
                        AND RemainingQuantity >= {allocation.Quantity}
                        AND IsActive = 1
                        AND IsDeleted = 0
                      """,
                    cancellationToken);
                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        $"Lô #{allocation.Lot.LotId} vừa bị thay đổi bởi giao dịch khác.");
                }
                allocation.Lot.RemainingQuantity -= allocation.Quantity;

                decimal totalCost = InventoryFinancialCalculator.RoundMoney(
                    allocation.Quantity * allocation.Lot.UnitCost);
                decimal profit = InventoryFinancialCalculator.RoundMoney(
                    allocationNetRevenue - totalCost - shippingShare);

                createdDetails.Add(new SalesOrderDetails
                {
                    SOId = salesOrder.SOId,
                    VariantId = allocation.VariantId,
                    LotId = allocation.Lot.LotId,
                    Quantity = allocation.Quantity,
                    UnitPrice = allocation.UnitPrice,
                    TaxRate = allocation.TaxRate,
                    UnitCost = allocation.Lot.UnitCost,
                    TotalAmount = allocationNetRevenue,
                    TotalCost = totalCost,
                    Profit = profit
                });

                var serials = await _context.ProductSerials
                    .Where(serial =>
                        serial.VariantId == allocation.VariantId
                        && serial.LotId == allocation.Lot.LotId
                        && serial.Status == "InStock")
                    .OrderBy(serial => serial.CreatedDate)
                    .ThenBy(serial => serial.SerialId)
                    .Take(allocation.Quantity)
                    .ToListAsync(cancellationToken);
                if (serials.Count != allocation.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Lô #{allocation.Lot.LotId} không đủ serial InStock.");
                }
                foreach (var serial in serials)
                {
                    serial.Status = "Dispatched";
                    serial.SoldDate = now;
                }

                _context.InventoryTransactions.Add(
                    new InventoryTransactions
                    {
                        VariantId = allocation.VariantId,
                        TransactionType = "OUT_DISTRIBUTION_FIFO",
                        Quantity = -allocation.Quantity,
                        ReferenceId = salesOrder.SOId,
                        TransactionDate = now,
                        AccountId = GetCurrentAccountId(),
                        WarehouseId = request.FromWarehouseId,
                        LotId = allocation.Lot.LotId,
                        UnitCostSnapshot = allocation.Lot.UnitCost,
                        TotalCostSnapshot = totalCost,
                        Note =
                            $"Xuất phân phối {salesOrder.SOCode}; lô #{allocation.Lot.LotId}; "
                            + $"lợi nhuận dòng {profit:N2} đ."
                    });
            }

            _context.SalesOrderDetails.AddRange(createdDetails);
            foreach (var variantId in plan.Allocations
                         .Select(item => item.VariantId)
                         .Distinct())
            {
                var variant = plan.Variants[variantId];
                variant.Stock = await _context.InventoryLots
                    .Where(lot =>
                        lot.VariantId == variantId
                        && lot.IsActive
                        && !lot.IsDeleted)
                    .SumAsync(
                        lot => (int?)lot.RemainingQuantity,
                        cancellationToken)
                    ?? 0;
            }

            decimal detailProfit = createdDetails.Sum(item => item.Profit);
            decimal roundingDifference = InventoryFinancialCalculator.RoundMoney(
                salesOrder.ProfitTotal - detailProfit);
            if (createdDetails.Count > 0 && roundingDifference != 0m)
            {
                createdDetails[^1].Profit = InventoryFinancialCalculator.RoundMoney(
                    createdDetails[^1].Profit + roundingDifference);
                detailProfit = createdDetails.Sum(item => item.Profit);
            }

            if (Math.Abs(detailProfit - salesOrder.ProfitTotal) > 0.01m)
            {
                throw new InvalidOperationException(
                    $"Đối soát lợi nhuận chi tiết lệch {detailProfit - salesOrder.ProfitTotal:N2} đ.");
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                soId = salesOrder.SOId,
                soCode = salesOrder.SOCode,
                warehouseId = request.FromWarehouseId,
                profit = salesOrder.ProfitTotal,
                realizedProfit = salesOrder.ProfitTotal,
                netRevenue = plan.Financial.NetSalesBeforeTax,
                taxAmount = plan.Financial.OutputVatAmount,
                invoiceTotal = plan.Financial.InvoiceTotal,
                cogs = plan.Financial.Cogs,
                shippingCost = plan.Financial.ShippingCost,
                marginPercent = plan.Financial.MarginPercent,
                warehouseScopeApplied = true
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Phase B SubmitSO failed.");
            return Json(Fail("Không thể xuất kho: " + exception.Message));
        }
    }

    [HttpPost("AdjustStock", Order = -200)]
    public async Task<IActionResult> AdjustStock(
        int variantId,
        int actualStock,
        string note,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        if (variantId <= 0 || actualStock < 0)
        {
            return Json(Fail("Biến thể hoặc tồn thực tế không hợp lệ."));
        }
        if (string.IsNullOrWhiteSpace(note))
        {
            return Json(Fail("Bắt buộc nhập lý do kiểm kê."));
        }

        int selectedWarehouseId;
        try
        {
            selectedWarehouseId = await ResolveWarehouseIdAsync(
                warehouseId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return Json(Fail(exception.Message));
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(
                    item => item.VariantId == variantId,
                    cancellationToken);
            if (variant == null)
            {
                return Json(Fail("Không tìm thấy biến thể."));
            }

            var lots = await _context.InventoryLots
                .Where(lot =>
                    lot.WarehouseId == selectedWarehouseId
                    && lot.VariantId == variantId
                    && lot.IsActive
                    && !lot.IsDeleted)
                .OrderBy(lot => lot.ReceivedDate)
                .ThenBy(lot => lot.LotId)
                .ToListAsync(cancellationToken);
            int currentWarehouseStock = lots.Sum(lot =>
                Math.Max(0, lot.RemainingQuantity));
            int difference = actualStock - currentWarehouseStock;
            var now = DateTime.Now;

            if (difference > 0)
            {
                decimal weightedCost = currentWarehouseStock > 0
                    ? InventoryFinancialCalculator.RoundMoney(
                        lots.Sum(lot => lot.RemainingQuantity * lot.UnitCost)
                        / currentWarehouseStock)
                    : variant.CostPrice ?? 0m;

                var adjustmentLot = new InventoryLots
                {
                    Poid = 0,
                    VariantId = variantId,
                    SupplierId = 1,
                    WarehouseId = selectedWarehouseId,
                    ReceivedQuantity = difference,
                    RemainingQuantity = difference,
                    UnitCost = weightedCost,
                    ReceivedDate = now,
                    IsActive = true,
                    IsDeleted = false
                };
                _context.InventoryLots.Add(adjustmentLot);
                await _context.SaveChangesAsync(cancellationToken);

                for (int index = 1; index <= difference; index++)
                {
                    _context.ProductSerials.Add(new ProductSerials
                    {
                        VariantId = variantId,
                        LotId = adjustmentLot.LotId,
                        SerialNumber =
                            $"ADJ-{variantId}-{adjustmentLot.LotId}-{index:0000}",
                        Status = "InStock",
                        CreatedDate = now
                    });
                }

                _context.InventoryTransactions.Add(
                    new InventoryTransactions
                    {
                        VariantId = variantId,
                        TransactionType = "IN_STOCKTAKE",
                        Quantity = difference,
                        TransactionDate = now,
                        AccountId = GetCurrentAccountId(),
                        WarehouseId = selectedWarehouseId,
                        LotId = adjustmentLot.LotId,
                        UnitCostSnapshot = weightedCost,
                        TotalCostSnapshot =
                            InventoryFinancialCalculator.RoundMoney(
                                weightedCost * difference),
                        Note =
                            $"Kiểm kê tăng kho #{selectedWarehouseId}: {note}; "
                            + $"tạo lô điều chỉnh #{adjustmentLot.LotId}."
                    });
            }
            else if (difference < 0)
            {
                int remainingReduction = Math.Abs(difference);
                foreach (var lot in lots)
                {
                    if (remainingReduction <= 0)
                    {
                        break;
                    }

                    int take = Math.Min(lot.RemainingQuantity, remainingReduction);
                    var serials = await _context.ProductSerials
                        .Where(serial =>
                            serial.LotId == lot.LotId
                            && serial.Status == "InStock")
                        .OrderBy(serial => serial.SerialId)
                        .Take(take)
                        .ToListAsync(cancellationToken);
                    if (serials.Count != take)
                    {
                        throw new InvalidOperationException(
                            $"Lô #{lot.LotId} lệch serial; không thể kiểm kê giảm {take}.");
                    }

                    lot.RemainingQuantity -= take;
                    remainingReduction -= take;
                    foreach (var serial in serials)
                    {
                        serial.Status = "AdjustedOut";
                    }

                    _context.InventoryTransactions.Add(
                        new InventoryTransactions
                        {
                            VariantId = variantId,
                            TransactionType = "OUT_STOCKTAKE_FIFO",
                            Quantity = -take,
                            TransactionDate = now,
                            AccountId = GetCurrentAccountId(),
                            WarehouseId = selectedWarehouseId,
                            LotId = lot.LotId,
                            UnitCostSnapshot = lot.UnitCost,
                            TotalCostSnapshot =
                                InventoryFinancialCalculator.RoundMoney(
                                    lot.UnitCost * take),
                            Note =
                                $"Kiểm kê giảm kho #{selectedWarehouseId}: {note}; "
                                + $"FIFO lô #{lot.LotId}."
                        });
                }

                if (remainingReduction > 0)
                {
                    throw new InvalidOperationException(
                        "Tồn lô không đủ để hoàn tất kiểm kê giảm.");
                }
            }

            variant.Stock = await CalculateAggregateVariantStockAsync(
                variantId,
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                warehouseId = selectedWarehouseId,
                warehouseStock = actualStock,
                aggregateStock = variant.Stock,
                diff = difference
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Phase B AdjustStock failed.");
            return Json(Fail("Không thể kiểm kê: " + exception.Message));
        }
    }

    [HttpPost("UpdateLotDetail", Order = -200)]
    public async Task<IActionResult> UpdateLotDetail(
        [FromForm] InventoryWarehouseUpdateLotRequest request,
        CancellationToken cancellationToken)
    {
        if (request.LotId <= 0
            || request.VariantId <= 0
            || request.RemainingQuantity < 0
            || request.UnitCost < 0m
            || request.Price < 0m)
        {
            return Json(Fail("Thông tin lô hoặc số tiền không hợp lệ."));
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var lot = await _context.InventoryLots
                .FirstOrDefaultAsync(
                    item =>
                        item.LotId == request.LotId
                        && item.VariantId == request.VariantId,
                    cancellationToken);
            var variant = await _context.ProductVariants
                .Include(item => item.Product)
                .FirstOrDefaultAsync(
                    item => item.VariantId == request.VariantId,
                    cancellationToken);
            if (lot == null || variant == null)
            {
                return Json(Fail("Không tìm thấy lô hoặc biến thể."));
            }

            int requestedWarehouseId = request.WarehouseId > 0
                ? request.WarehouseId
                : lot.WarehouseId;
            if (requestedWarehouseId != lot.WarehouseId)
            {
                return Json(Fail(
                    "Không được sửa WarehouseId trực tiếp trên lô vì sẽ làm sai lịch sử. Hãy dùng chức năng TransferLot."));
            }

            int difference = request.RemainingQuantity - lot.RemainingQuantity;
            int oldRemaining = lot.RemainingQuantity;
            decimal oldCost = lot.UnitCost;
            var now = DateTime.Now;

            if (difference > 0)
            {
                for (int index = 1; index <= difference; index++)
                {
                    _context.ProductSerials.Add(new ProductSerials
                    {
                        VariantId = variant.VariantId,
                        LotId = lot.LotId,
                        SerialNumber =
                            $"LOT-CORR-{lot.LotId}-{Guid.NewGuid():N}",
                        Status = "InStock",
                        CreatedDate = now
                    });
                }
            }
            else if (difference < 0)
            {
                int quantity = Math.Abs(difference);
                var serials = await _context.ProductSerials
                    .Where(serial =>
                        serial.LotId == lot.LotId
                        && serial.Status == "InStock")
                    .OrderBy(serial => serial.SerialId)
                    .Take(quantity)
                    .ToListAsync(cancellationToken);
                if (serials.Count != quantity)
                {
                    throw new InvalidOperationException(
                        "Số serial InStock không đủ để giảm tồn lô.");
                }
                foreach (var serial in serials)
                {
                    serial.Status = "AdjustedOut";
                }
            }

            lot.RemainingQuantity = request.RemainingQuantity;
            lot.UnitCost = request.UnitCost;
            variant.Price = request.Price;
            variant.CostPrice = request.UnitCost;

            if (!string.IsNullOrWhiteSpace(request.ProductName)
                && variant.Product != null)
            {
                variant.Product.Name = request.ProductName.Trim();
            }

            if (request.ImageFile is { Length: > 0 })
            {
                string folder = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads",
                    "products");
                Directory.CreateDirectory(folder);
                string fileName =
                    $"{Guid.NewGuid():N}{Path.GetExtension(request.ImageFile.FileName)}";
                string path = Path.Combine(folder, fileName);
                await using var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                await request.ImageFile.CopyToAsync(stream, cancellationToken);
                variant.ImageUrl = "/uploads/products/" + fileName;
            }
            else if (!string.IsNullOrWhiteSpace(request.ImageUrl))
            {
                variant.ImageUrl = request.ImageUrl.Trim();
            }

            variant.Stock = await CalculateAggregateVariantStockAsync(
                variant.VariantId,
                cancellationToken);

            _context.InventoryTransactions.Add(
                new InventoryTransactions
                {
                    VariantId = variant.VariantId,
                    TransactionType = "LOT_CORRECTION",
                    Quantity = difference,
                    TransactionDate = now,
                    AccountId = GetCurrentAccountId(),
                    WarehouseId = lot.WarehouseId,
                    LotId = lot.LotId,
                    UnitCostSnapshot = lot.UnitCost,
                    TotalCostSnapshot =
                        InventoryFinancialCalculator.RoundMoney(
                            lot.UnitCost * Math.Abs(difference)),
                    Note =
                        $"Sửa lô #{lot.LotId}: tồn {oldRemaining}->{lot.RemainingQuantity}; "
                        + $"vốn {oldCost:N2}->{lot.UnitCost:N2}."
                });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Json(new
            {
                success = true,
                message = "Đã cập nhật lô và đồng bộ serial/tồn tổng.",
                warehouseId = lot.WarehouseId,
                aggregateStock = variant.Stock
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Phase B UpdateLotDetail failed.");
            return Json(Fail("Không thể cập nhật lô: " + exception.Message));
        }
    }

    [HttpPost("TransferLot", Order = -200)]
    public async Task<IActionResult> TransferLot(
        [FromBody] InventoryTransferLotRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null
            || request.LotId <= 0
            || request.TargetWarehouseId <= 0
            || request.Quantity <= 0
            || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Json(Fail("Thông tin luân chuyển kho không hợp lệ."));
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var sourceLot = await _context.InventoryLots
                .FirstOrDefaultAsync(
                    item =>
                        item.LotId == request.LotId
                        && item.IsActive
                        && !item.IsDeleted,
                    cancellationToken);
            if (sourceLot == null)
            {
                return Json(Fail("Không tìm thấy lô nguồn đang hoạt động."));
            }
            if (sourceLot.WarehouseId == request.TargetWarehouseId)
            {
                return Json(Fail("Kho đích phải khác kho nguồn."));
            }
            bool targetExists = await _context.Warehouses.AnyAsync(
                item =>
                    item.WarehouseId == request.TargetWarehouseId
                    && item.IsActive,
                cancellationToken);
            if (!targetExists)
            {
                return Json(Fail("Kho đích không tồn tại hoặc đã ngừng hoạt động."));
            }
            if (sourceLot.RemainingQuantity < request.Quantity)
            {
                return Json(Fail(
                    $"Lô nguồn chỉ còn {sourceLot.RemainingQuantity} sản phẩm."));
            }

            var serials = await _context.ProductSerials
                .Where(serial =>
                    serial.LotId == sourceLot.LotId
                    && serial.Status == "InStock")
                .OrderBy(serial => serial.SerialId)
                .Take(request.Quantity)
                .ToListAsync(cancellationToken);
            if (serials.Count != request.Quantity)
            {
                throw new InvalidOperationException(
                    "Số serial InStock của lô nguồn không đủ để luân chuyển.");
            }

            sourceLot.RemainingQuantity -= request.Quantity;
            var targetLot = new InventoryLots
            {
                Poid = sourceLot.Poid,
                VariantId = sourceLot.VariantId,
                SupplierId = sourceLot.SupplierId,
                WarehouseId = request.TargetWarehouseId,
                ReceivedQuantity = request.Quantity,
                RemainingQuantity = request.Quantity,
                UnitCost = sourceLot.UnitCost,
                // Giữ tuổi hàng gốc để FIFO không bị làm mới bởi luân chuyển nội bộ.
                ReceivedDate = sourceLot.ReceivedDate,
                IsActive = true,
                IsDeleted = false
            };
            _context.InventoryLots.Add(targetLot);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var serial in serials)
            {
                serial.LotId = targetLot.LotId;
            }

            decimal totalCost = InventoryFinancialCalculator.RoundMoney(
                sourceLot.UnitCost * request.Quantity);
            var now = DateTime.Now;
            _context.InventoryTransactions.AddRange(
                new InventoryTransactions
                {
                    VariantId = sourceLot.VariantId,
                    TransactionType = "OUT_TRANSFER",
                    Quantity = -request.Quantity,
                    TransactionDate = now,
                    AccountId = GetCurrentAccountId(),
                    WarehouseId = sourceLot.WarehouseId,
                    LotId = sourceLot.LotId,
                    UnitCostSnapshot = sourceLot.UnitCost,
                    TotalCostSnapshot = totalCost,
                    Note =
                        $"Chuyển sang kho #{request.TargetWarehouseId}; lý do: {request.Reason.Trim()}."
                },
                new InventoryTransactions
                {
                    VariantId = sourceLot.VariantId,
                    TransactionType = "IN_TRANSFER",
                    Quantity = request.Quantity,
                    TransactionDate = now,
                    AccountId = GetCurrentAccountId(),
                    WarehouseId = request.TargetWarehouseId,
                    LotId = targetLot.LotId,
                    UnitCostSnapshot = sourceLot.UnitCost,
                    TotalCostSnapshot = totalCost,
                    Note =
                        $"Nhận từ kho #{sourceLot.WarehouseId}, lô #{sourceLot.LotId}; "
                        + $"lý do: {request.Reason.Trim()}."
                });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Json(new
            {
                success = true,
                sourceLotId = sourceLot.LotId,
                targetLotId = targetLot.LotId,
                sourceWarehouseId = sourceLot.WarehouseId,
                targetWarehouseId = targetLot.WarehouseId,
                quantity = request.Quantity,
                message = "Luân chuyển kho thành công; tổng tồn SKU không thay đổi."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Phase B TransferLot failed.");
            return Json(Fail("Không thể luân chuyển kho: " + exception.Message));
        }
    }

    [HttpGet("LotLocations", Order = -200)]
    public async Task<IActionResult> LotLocations(
        CancellationToken cancellationToken)
    {
        var result = await _context.InventoryLots
            .AsNoTracking()
            .Include(item => item.Warehouse)
            .Select(item => new
            {
                lotId = item.LotId,
                warehouseId = item.WarehouseId,
                warehouseName = item.Warehouse != null
                    ? item.Warehouse.WarehouseName
                    : $"Kho #{item.WarehouseId}"
            })
            .ToListAsync(cancellationToken);
        return Json(result);
    }

    [HttpGet("FinancialOverview", Order = -200)]
    public async Task<IActionResult> FinancialOverview(
        int? year,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        DateTime start = fromDate?.Date
            ?? new DateTime(year ?? DateTime.Now.Year, 1, 1);
        DateTime endExclusive = toDate?.Date.AddDays(1)
            ?? start.AddYears(1);

        var activeLots = await _context.InventoryLots
            .AsNoTracking()
            .Include(lot => lot.Variant)
            .Include(lot => lot.Warehouse)
            .Where(lot =>
                lot.IsActive
                && !lot.IsDeleted
                && lot.RemainingQuantity > 0)
            .ToListAsync(cancellationToken);

        decimal inventoryCost = activeLots.Sum(lot =>
            lot.RemainingQuantity * lot.UnitCost);
        decimal inventoryMarket = activeLots.Sum(lot =>
        {
            decimal price = lot.Variant?.DiscountPrice is > 0m
                ? lot.Variant.DiscountPrice.Value
                : lot.Variant?.Price ?? 0m;
            return lot.RemainingQuantity * price;
        });

        var lotStockByVariant = activeLots
            .GroupBy(lot => lot.VariantId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(lot => lot.RemainingQuantity));
        var variantStocks = await _context.ProductVariants
            .AsNoTracking()
            .Select(item => new
            {
                item.VariantId,
                Stock = item.Stock ?? 0
            })
            .ToListAsync(cancellationToken);
        int stockMismatchCount = variantStocks.Count(item =>
            item.Stock != lotStockByVariant.GetValueOrDefault(item.VariantId));

        var serialCounts = await _context.ProductSerials
            .AsNoTracking()
            .Where(serial => serial.Status == "InStock")
            .GroupBy(serial => serial.LotId)
            .Select(group => new
            {
                LotId = group.Key,
                Quantity = group.Count()
            })
            .ToDictionaryAsync(
                item => item.LotId,
                item => item.Quantity,
                cancellationToken);
        int serialMismatchCount = activeLots.Count(lot =>
            serialCounts.GetValueOrDefault(lot.LotId)
            != lot.RemainingQuantity);

        var completedSales = await _context.SalesOrders
            .AsNoTracking()
            .Where(order =>
                order.Status == "Completed"
                && order.OrderDate >= start
                && order.OrderDate < endExclusive
                && order.Notes != null
                && order.Notes.Contains("ProfitTotal = doanh thu thuần chưa VAT"))
            .ToListAsync(cancellationToken);
        decimal invoiceTotal = completedSales.Sum(order => order.TotalAmount);
        decimal outputVat = completedSales.Sum(order => order.TaxAmount);
        decimal netRevenue = invoiceTotal - outputVat;
        decimal cogs = completedSales.Sum(order => order.COGSTotal);
        decimal shipping = completedSales.Sum(order => order.ShippingCost);
        decimal recognizedProfit = completedSales.Sum(order => order.ProfitTotal);

        var online = await OrderProfitService.GetOverviewAsync(
            _context,
            start,
            endExclusive,
            cancellationToken);

        var warehouseSummary = activeLots
            .GroupBy(lot => new
            {
                lot.WarehouseId,
                Name = lot.Warehouse?.WarehouseName
                    ?? $"Kho #{lot.WarehouseId}"
            })
            .Select(group => new
            {
                warehouseId = group.Key.WarehouseId,
                warehouseName = group.Key.Name,
                quantity = group.Sum(lot => lot.RemainingQuantity),
                inventoryCost = InventoryFinancialCalculator.RoundMoney(
                    group.Sum(lot => lot.RemainingQuantity * lot.UnitCost))
            })
            .OrderBy(item => item.warehouseId)
            .ToList();

        return Json(new
        {
            success = true,
            start,
            endExclusive,
            inventoryCost = InventoryFinancialCalculator.RoundMoney(inventoryCost),
            inventoryMarket = InventoryFinancialCalculator.RoundMoney(inventoryMarket),
            potentialGrossMargin = InventoryFinancialCalculator.RoundMoney(
                inventoryMarket - inventoryCost),
            completedSalesCount = completedSales.Count,
            invoiceTotal = InventoryFinancialCalculator.RoundMoney(invoiceTotal),
            netRevenueBeforeVat = InventoryFinancialCalculator.RoundMoney(netRevenue),
            outputVat = InventoryFinancialCalculator.RoundMoney(outputVat),
            cogs = InventoryFinancialCalculator.RoundMoney(cogs),
            shippingCost = InventoryFinancialCalculator.RoundMoney(shipping),
            recognizedProfit = InventoryFinancialCalculator.RoundMoney(recognizedProfit),
            stockMismatchCount,
            serialMismatchCount,
            warehouses = warehouseSummary,
            onlineOrderCount = online.OrderCount,
            onlinePaidOrderCount = online.PaidOrderCount,
            onlineCostedOrderCount = online.CostedOrderCount,
            onlineRecognizedOrderCount = online.RecognizedOrderCount,
            onlineExcludedPendingOrderCount = online.ExcludedPendingOrderCount,
            onlinePaidAmount = online.PaidAmount,
            onlineRefundAmount = online.RefundAmount,
            onlineNetCashCollected = online.NetCashCollected,
            onlineOutstandingAmount = online.OutstandingAmount,
            onlineActiveCogs = online.ActiveCogs,
            onlineCarrierShippingExpense = online.CarrierShippingExpense,
            onlineShippingCostProxy = online.CarrierShippingExpense,
            onlineCashContributionProfit = online.CashContributionProfit,
            onlineProfitLimitation = online.Limitation,
            basis =
                "Phiếu phân phối: doanh thu thuần chưa VAT - FIFO COGS - shipping cost. "
                + "Đơn bán lẻ: chỉ ghi nhận doanh thu khi đã giao/hoàn thành; "
                + "tiền thu = thanh toán thành công - hoàn tiền; "
                + "shipping hiện là snapshot/proxy và chưa trừ phí cổng thanh toán."
        });
    }

    private async Task<WarehouseSalesPlan> BuildSalesPlanAsync(
        InventoryWarehouseSoRequest request,
        bool tracking,
        CancellationToken cancellationToken)
    {
        int warehouseId = await ResolveWarehouseIdAsync(
            request.FromWarehouseId,
            cancellationToken);
        request.FromWarehouseId = warehouseId;

        var normalizedItems = request.Items
            .Where(item => item.Quantity > 0 && item.ExportPrice > 0m)
            .GroupBy(item => item.VariantId)
            .Select(group =>
            {
                var first = group.First();
                if (group.Any(item =>
                        item.ExportPrice != first.ExportPrice
                        || item.TaxRate != first.TaxRate))
                {
                    throw new InvalidOperationException(
                        $"Biến thể #{first.VariantId} có nhiều giá hoặc thuế suất khác nhau.");
                }

                return new InventoryWarehouseSoItemRequest
                {
                    VariantId = first.VariantId,
                    Quantity = group.Sum(item => item.Quantity),
                    ExportPrice = first.ExportPrice,
                    TaxRate = first.TaxRate
                };
            })
            .ToList();

        var variantIds = normalizedItems.Select(item => item.VariantId).ToList();
        IQueryable<ProductVariants> variantQuery = _context.ProductVariants
            .Where(item => variantIds.Contains(item.VariantId));
        if (!tracking)
        {
            variantQuery = variantQuery.AsNoTracking();
        }
        var variants = await variantQuery.ToDictionaryAsync(
            item => item.VariantId,
            cancellationToken);
        if (variants.Count != variantIds.Count)
        {
            throw new InvalidOperationException("Có biến thể không tồn tại.");
        }

        var allocations = new List<WarehouseFifoSalesAllocation>();
        decimal totalCogs = 0m;
        foreach (var item in normalizedItems)
        {
            IQueryable<InventoryLots> lotQuery = _context.InventoryLots
                .Where(lot =>
                    lot.WarehouseId == warehouseId
                    && lot.VariantId == item.VariantId
                    && lot.RemainingQuantity > 0
                    && lot.IsActive
                    && !lot.IsDeleted)
                .OrderBy(lot => lot.ReceivedDate)
                .ThenBy(lot => lot.LotId);
            if (!tracking)
            {
                lotQuery = lotQuery.AsNoTracking();
            }
            var lots = await lotQuery.ToListAsync(cancellationToken);
            int available = lots.Sum(lot => lot.RemainingQuantity);
            if (available < item.Quantity)
            {
                throw new InvalidOperationException(
                    $"Kho #{warehouseId} chỉ còn {available} sản phẩm của biến thể #{item.VariantId}, cần {item.Quantity}.");
            }

            int remaining = item.Quantity;
            decimal allocatedNetRevenue = 0m;
            foreach (var lot in lots)
            {
                if (remaining <= 0)
                {
                    break;
                }
                int quantity = Math.Min(lot.RemainingQuantity, remaining);
                remaining -= quantity;
                allocations.Add(new WarehouseFifoSalesAllocation(
                    item.VariantId,
                    variants[item.VariantId],
                    lot,
                    quantity,
                    item.ExportPrice,
                    item.TaxRate,
                    remaining == 0,
                    allocatedNetRevenue));
                totalCogs += quantity * lot.UnitCost;

                decimal gross = item.Quantity * item.ExportPrice;
                decimal discount = gross
                    * Math.Clamp(request.DiscountPercent, 0m, 100m)
                    / 100m;
                decimal netLine = gross - discount;
                if (remaining > 0)
                {
                    allocatedNetRevenue += InventoryFinancialCalculator.RoundMoney(
                        netLine * quantity / item.Quantity);
                }
            }
        }

        var financial = InventoryFinancialCalculator.CalculateSalesFinancials(
            normalizedItems.Select(item => new SalesLineInput(
                item.VariantId,
                item.Quantity,
                item.ExportPrice,
                item.TaxRate)).ToList(),
            request.DiscountPercent,
            request.ShippingCost,
            totalCogs);

        return new WarehouseSalesPlan(
            allocations,
            financial,
            variants);
    }

    private static object? ValidateSalesRequest(
        InventoryWarehouseSoRequest? request)
    {
        if (request == null || request.Items.Count == 0)
        {
            return Fail("Phiếu xuất chưa có mặt hàng hợp lệ.");
        }
        if (request.StoreId <= 0 || request.FromWarehouseId <= 0)
        {
            return Fail("Cửa hàng nhận và kho xuất là bắt buộc.");
        }
        if (request.DiscountPercent < 0m
            || request.DiscountPercent > 100m
            || request.ShippingCost < 0m)
        {
            return Fail("Chiết khấu hoặc chi phí vận chuyển không hợp lệ.");
        }
        if (request.Items.Any(item =>
                item.VariantId <= 0
                || item.Quantity <= 0
                || item.ExportPrice <= 0m))
        {
            return Fail("Dòng xuất có biến thể, số lượng hoặc giá không hợp lệ.");
        }
        return null;
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
                    $"Kho #{requestedWarehouseId.Value} không tồn tại hoặc đã ngừng hoạt động.");
            }
            return requestedWarehouseId.Value;
        }

        int primaryWarehouseId = await _context.Warehouses
            .Where(item => item.IsActive)
            .OrderByDescending(item => item.IsPrimary)
            .ThenBy(item => item.WarehouseId)
            .Select(item => item.WarehouseId)
            .FirstOrDefaultAsync(cancellationToken);

        if (primaryWarehouseId <= 0)
        {
            throw new InvalidOperationException(
                "Hệ thống chưa có kho hoạt động.");
        }

        return primaryWarehouseId;
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
            .SumAsync(
                lot => (int?)lot.RemainingQuantity,
                cancellationToken)
            ?? 0;
    }

    private int GetCurrentAccountId()
    {
        string? value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out int accountId)
            ? accountId
            : 0;
    }

    private static string BuildSalesOrderNote(string? notes)
    {
        return string.Join(
            " | ",
            new[]
            {
                "ProfitTotal = doanh thu thuần chưa VAT - FIFO COGS - chi phí vận chuyển",
                "TotalAmount = doanh thu thuần + VAT đầu ra",
                string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static object Fail(string message) => new
    {
        success = false,
        message
    };

    private sealed record WarehouseFifoSalesAllocation(
        int VariantId,
        ProductVariants Variant,
        InventoryLots Lot,
        int Quantity,
        decimal UnitPrice,
        decimal TaxRate,
        bool IsLastForVariant,
        decimal NetRevenueAlreadyAllocated);

    private sealed record WarehouseSalesPlan(
        List<WarehouseFifoSalesAllocation> Allocations,
        SalesFinancialSummary Financial,
        Dictionary<int, ProductVariants> Variants);
}

public sealed class InventoryWarehousePoItemRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal ImportPrice { get; set; }
    public decimal TaxRate { get; set; }
}

public sealed class InventoryWarehousePoRequest
{
    public int SupplierId { get; set; }
    public int WarehouseId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal OtherFee { get; set; }
    public bool InputVatDeductible { get; set; } = true;
    public string? Note { get; set; }
    public List<InventoryWarehousePoItemRequest> Items { get; set; } = new();
}

public sealed class InventoryWarehouseSoItemRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal ExportPrice { get; set; }
    public decimal TaxRate { get; set; }
}

public sealed class InventoryWarehouseSoRequest
{
    public int StoreId { get; set; }
    public int FromWarehouseId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal ShippingCost { get; set; }
    public string? Notes { get; set; }
    public List<InventoryWarehouseSoItemRequest> Items { get; set; } = new();
}

public sealed class InventoryWarehouseUpdateLotRequest
{
    public int LotId { get; set; }
    public int VariantId { get; set; }
    public int WarehouseId { get; set; }
    public string? ProductName { get; set; }
    public string? VariantName { get; set; }
    public int RemainingQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Price { get; set; }
    public string? StoreLocation { get; set; }
    public string? ImageUrl { get; set; }
    public IFormFile? ImageFile { get; set; }
}

public sealed class InventoryTransferLotRequest
{
    public int LotId { get; set; }
    public int TargetWarehouseId { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}
