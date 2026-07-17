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
/// Lớp route nghiệp vụ có độ ưu tiên cao hơn conventional route cũ của
/// InventoryController. Mục tiêu là sửa công thức giá vốn, FIFO và lợi nhuận
/// mà không phải thay toàn bộ controller lớn đang có.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory")]
public sealed class InventoryBusinessController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryBusinessController> _logger;

    public InventoryBusinessController(
        ApplicationDbContext context,
        ILogger<InventoryBusinessController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("SearchVariants", Order = -100)]
    public async Task<IActionResult> SearchVariants(
        string q,
        CancellationToken cancellationToken)
    {
        q = (q ?? string.Empty).Trim();
        if (q.Length < 2)
        {
            return Json(Array.Empty<object>());
        }

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(variant => variant.Product)
            .Where(variant =>
                variant.IsActive == true
                && variant.Product != null
                && (variant.Product.Name.Contains(q)
                    || (variant.Color != null && variant.Color.Contains(q))
                    || (variant.Storage != null && variant.Storage.Contains(q))
                    || (variant.Ram != null && variant.Ram.Contains(q))))
            .Take(10)
            .ToListAsync(cancellationToken);

        var variantIds = variants
            .Select(variant => variant.VariantId)
            .ToList();

        var activeLots = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot =>
                variantIds.Contains(lot.VariantId)
                && lot.IsActive
                && !lot.IsDeleted)
            .ToListAsync(cancellationToken);

        var result = variants.Select(variant =>
        {
            var lots = activeLots
                .Where(lot => lot.VariantId == variant.VariantId)
                .ToList();
            int lotStock = lots.Sum(lot => Math.Max(0, lot.RemainingQuantity));
            var latestCost = lots
                .OrderByDescending(lot => lot.ReceivedDate)
                .ThenByDescending(lot => lot.LotId)
                .Select(lot => (decimal?)lot.UnitCost)
                .FirstOrDefault();
            int aggregateStock = Math.Max(0, variant.Stock ?? 0);

            return new
            {
                variantId = variant.VariantId,
                productName = variant.Product!.Name,
                color = variant.Color,
                storage = variant.Storage
                    + (string.IsNullOrWhiteSpace(variant.Ram)
                        ? string.Empty
                        : $" ({variant.Ram})"),
                stock = lotStock,
                aggregateStock,
                stockMismatch = aggregateStock != lotStock,
                lastImportPrice = latestCost ?? variant.CostPrice ?? 0m
            };
        });

        return Json(result);
    }

    [HttpPost("SubmitPO", Order = -100)]
    public async Task<IActionResult> SubmitPO(
        [FromBody] InventoryPoRequest? request,
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

        if (request.ShippingFee < 0m || request.OtherFee < 0m)
        {
            return Json(Fail("Chi phí vận chuyển và chi phí phát sinh không được âm."));
        }

        var normalizedItems = request.Items
            .Where(item => item.Quantity > 0 && item.ImportPrice > 0m)
            .GroupBy(item => item.VariantId)
            .Select(group =>
            {
                var first = group.First();
                bool inconsistent = group.Any(item =>
                    item.ImportPrice != first.ImportPrice
                    || item.TaxRate != first.TaxRate);

                return new
                {
                    first.VariantId,
                    Quantity = group.Sum(item => item.Quantity),
                    first.ImportPrice,
                    first.TaxRate,
                    Inconsistent = inconsistent
                };
            })
            .ToList();

        if (normalizedItems.Count == 0)
        {
            return Json(Fail("Không có dòng nhập nào có số lượng và giá nhập hợp lệ."));
        }

        if (normalizedItems.Any(item => item.Inconsistent))
        {
            return Json(Fail(
                "Một biến thể đang xuất hiện nhiều lần với giá hoặc thuế suất khác nhau. Hãy gộp thành một dòng."));
        }

        bool supplierExists = await _context.Suppliers
            .AnyAsync(
                supplier =>
                    supplier.SupplierId == request.SupplierId
                    && supplier.IsActive == true,
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
            .Where(variant => variantIds.Contains(variant.VariantId))
            .ToDictionaryAsync(
                variant => variant.VariantId,
                cancellationToken);

        if (variants.Count != variantIds.Count)
        {
            return Json(Fail("Có biến thể không tồn tại trong hệ thống."));
        }

        PurchaseCostSummary costSummary;
        try
        {
            costSummary = InventoryFinancialCalculator.CalculatePurchaseCosts(
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
            .BeginTransactionAsync(cancellationToken);

        try
        {
            string invoiceReference = string.IsNullOrWhiteSpace(request.InvoiceNumber)
                ? "Không có số hóa đơn"
                : request.InvoiceNumber.Trim();
            string note = string.Join(
                " | ",
                new[]
                {
                    $"Hóa đơn: {invoiceReference}",
                    $"Ngày hóa đơn: {(request.InvoiceDate ?? now):dd/MM/yyyy}",
                    $"VAT đầu vào: {(request.InputVatDeductible ? "khấu trừ, không vốn hóa" : "không khấu trừ, đã vốn hóa")}",
                    $"Phí nhập phân bổ: {(request.ShippingFee + request.OtherFee):N0} đ",
                    string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
                }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var purchaseOrder = new PurchaseOrders
            {
                SupplierId = request.SupplierId,
                AccountId = GetCurrentAccountId(),
                OrderDate = request.InvoiceDate ?? now,
                TotalAmount = costSummary.SupplierPayable,
                Status = "Hoàn thành",
                Note = note
            };

            _context.PurchaseOrders.Add(purchaseOrder);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var costLine in costSummary.Lines)
            {
                var variant = variants[costLine.VariantId];

                _context.PurchaseOrderDetails.Add(new PurchaseOrderDetails
                {
                    Poid = purchaseOrder.Poid,
                    VariantId = costLine.VariantId,
                    Quantity = costLine.Quantity,
                    ImportPrice = costLine.ImportPrice
                });

                var lot = new InventoryLots
                {
                    Poid = purchaseOrder.Poid,
                    VariantId = costLine.VariantId,
                    SupplierId = request.SupplierId,
                    ReceivedQuantity = costLine.Quantity,
                    RemainingQuantity = costLine.Quantity,
                    UnitCost = costLine.LandedUnitCost,
                    ReceivedDate = receivedDate,
                    IsActive = true,
                    IsDeleted = false
                };

                _context.InventoryLots.Add(lot);
                await _context.SaveChangesAsync(cancellationToken);

                for (int serialIndex = 1;
                     serialIndex <= costLine.Quantity;
                     serialIndex++)
                {
                    _context.ProductSerials.Add(new ProductSerials
                    {
                        VariantId = costLine.VariantId,
                        LotId = lot.LotId,
                        SerialNumber =
                            $"IMEI-{costLine.VariantId}-{lot.LotId}-{serialIndex:0000}",
                        Status = "InStock",
                        CreatedDate = now
                    });
                }

                int quantityBefore = Math.Max(0, variant.Stock ?? 0);
                variant.Stock = checked(quantityBefore + costLine.Quantity);
                variant.CostPrice = costLine.LandedUnitCost;

                _context.InventoryTransactions.Add(new InventoryTransactions
                {
                    VariantId = costLine.VariantId,
                    TransactionType = "IN_PURCHASE",
                    Quantity = costLine.Quantity,
                    ReferenceId = purchaseOrder.Poid,
                    TransactionDate = now,
                    AccountId = GetCurrentAccountId(),
                    Note =
                        $"Nhập lô #{lot.LotId}; tồn {quantityBefore} -> {variant.Stock}; "
                        + $"giá vốn landed {costLine.LandedUnitCost:N2} đ/đơn vị."
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                poId = purchaseOrder.Poid,
                poCode = $"PO-{purchaseOrder.Poid:D8}",
                supplierPayable = costSummary.SupplierPayable,
                goodsSubtotal = costSummary.GoodsSubtotal,
                inputVatAmount = costSummary.InputVatAmount,
                inventoryCapitalizedCost = costSummary.InventoryCapitalizedCost,
                inputVatDeductible = costSummary.InputVatDeductible
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "SubmitPO business correction failed.");
            return Json(Fail("Không thể ghi nhận phiếu nhập: " + exception.Message));
        }
    }

    [HttpPost("EstimateSO", Order = -100)]
    public async Task<IActionResult> EstimateSO(
        [FromBody] InventorySoRequest? request,
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
                    "Lợi nhuận theo FIFO, loại VAT đầu ra và trừ chi phí vận chuyển; chưa phải dòng tiền đã thu nếu chưa có đối soát công nợ."
            });
        }
        catch (Exception exception)
        {
            return Json(Fail(exception.Message));
        }
    }

    [HttpPost("SubmitSO", Order = -100)]
    public async Task<IActionResult> SubmitSO(
        [FromBody] InventorySoRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateSalesRequest(request);
        if (validation != null)
        {
            return Json(validation);
        }

        bool storeExists = await _context.Stores.AnyAsync(
            store =>
                store.StoreId == request!.StoreId
                && store.IsActive == true,
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

            string salesOrderCode =
                $"SO-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            var salesOrder = new SalesOrders
            {
                SOCode = salesOrderCode,
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
                var lineFinancial = plan.Financial.Lines
                    .Single(line => line.VariantId == allocation.VariantId);

                decimal allocationNetRevenue = allocation.IsLastForVariant
                    ? InventoryFinancialCalculator.RoundMoney(
                        lineFinancial.NetBeforeTax - allocation.NetRevenueAlreadyAllocated)
                    : InventoryFinancialCalculator.RoundMoney(
                        lineFinancial.NetBeforeTax
                        * allocation.Quantity
                        / lineFinancial.Quantity);

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

                decimal totalCost = InventoryFinancialCalculator.RoundMoney(
                    allocation.Quantity * allocation.Lot.UnitCost);
                decimal allocationProfit = InventoryFinancialCalculator.RoundMoney(
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
                    // TotalAmount được chuẩn hóa thành doanh thu thuần chưa VAT.
                    TotalAmount = allocationNetRevenue,
                    TotalCost = totalCost,
                    Profit = allocationProfit
                });

                allocation.Lot.RemainingQuantity -= allocation.Quantity;
                if (allocation.Lot.RemainingQuantity < 0)
                {
                    throw new InvalidOperationException(
                        $"Lô #{allocation.Lot.LotId} bị âm tồn trong lúc xuất FIFO.");
                }

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
                        $"Lô #{allocation.Lot.LotId} không đủ serial InStock để xuất {allocation.Quantity} sản phẩm.");
                }

                foreach (var serial in serials)
                {
                    serial.Status = "Dispatched";
                    // ProductSerials.OrderId đang liên kết đơn bán lẻ Orders,
                    // không được gán SOId của phiếu phân phối.
                    serial.SoldDate = now;
                }
            }

            _context.SalesOrderDetails.AddRange(createdDetails);

            foreach (var variantGroup in plan.Allocations.GroupBy(item => item.VariantId))
            {
                var variant = variantGroup.First().Variant;
                int quantityBefore = Math.Max(0, variant.Stock ?? 0);
                int remainingByLots = plan.AllLotsByVariant[variant.VariantId]
                    .Where(lot => lot.IsActive && !lot.IsDeleted)
                    .Sum(lot => Math.Max(0, lot.RemainingQuantity));
                variant.Stock = remainingByLots;

                _context.InventoryTransactions.Add(new InventoryTransactions
                {
                    VariantId = variant.VariantId,
                    TransactionType = "OUT_ORDER",
                    Quantity = -variantGroup.Sum(item => item.Quantity),
                    ReferenceId = salesOrder.SOId,
                    TransactionDate = now,
                    AccountId = GetCurrentAccountId(),
                    Note =
                        $"Xuất FIFO phiếu {salesOrder.SOCode}; tồn {quantityBefore} -> {remainingByLots}; "
                        + $"các lô: {string.Join(", ", variantGroup.Select(item => item.Lot.LotId).Distinct())}."
                });
            }

            decimal detailProfit = createdDetails.Sum(detail => detail.Profit);
            if (Math.Abs(detailProfit - salesOrder.ProfitTotal) > 0.05m)
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
                profit = salesOrder.ProfitTotal,
                realizedProfit = salesOrder.ProfitTotal,
                netRevenue = plan.Financial.NetSalesBeforeTax,
                taxAmount = plan.Financial.OutputVatAmount,
                invoiceTotal = plan.Financial.InvoiceTotal,
                cogs = plan.Financial.Cogs,
                shippingCost = plan.Financial.ShippingCost,
                marginPercent = plan.Financial.MarginPercent,
                warehouseScopeApplied = false,
                warehouseWarning =
                    "Schema hiện tại chưa gắn WarehouseId vào từng lô; phiếu vẫn lưu kho nguồn nhưng FIFO đang chạy trên toàn bộ lô của biến thể."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "SubmitSO business correction failed.");
            return Json(Fail("Không thể xuất kho: " + exception.Message));
        }
    }

    [HttpPost("AdjustStock", Order = -100)]
    public async Task<IActionResult> AdjustStock(
        int variantId,
        int actualStock,
        string note,
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
                    lot.VariantId == variantId
                    && lot.IsActive
                    && !lot.IsDeleted)
                .OrderBy(lot => lot.ReceivedDate)
                .ThenBy(lot => lot.LotId)
                .ToListAsync(cancellationToken);

            int currentLotStock = lots.Sum(lot => Math.Max(0, lot.RemainingQuantity));
            int previousAggregateStock = Math.Max(0, variant.Stock ?? 0);
            int difference = actualStock - currentLotStock;
            var now = DateTime.Now;

            if (difference > 0)
            {
                decimal weightedCost = currentLotStock > 0
                    ? InventoryFinancialCalculator.RoundMoney(
                        lots.Sum(lot => lot.RemainingQuantity * lot.UnitCost)
                        / currentLotStock)
                    : Math.Max(0m, variant.CostPrice ?? 0m);
                var latestLot = lots
                    .OrderByDescending(lot => lot.ReceivedDate)
                    .ThenByDescending(lot => lot.LotId)
                    .FirstOrDefault();

                var adjustmentLot = new InventoryLots
                {
                    // Poid=0 là lô kiểm kê kỹ thuật trong schema cũ, không giả làm phiếu nhập NCC.
                    Poid = 0,
                    VariantId = variantId,
                    SupplierId = latestLot?.SupplierId ?? 1,
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
            }
            else if (difference < 0)
            {
                int quantityToRemove = Math.Abs(difference);
                if (currentLotStock < quantityToRemove)
                {
                    return Json(Fail("Tổng tồn theo lô không đủ để giảm theo kết quả kiểm kê."));
                }

                foreach (var lot in lots)
                {
                    if (quantityToRemove <= 0)
                    {
                        break;
                    }

                    int takeQuantity = Math.Min(lot.RemainingQuantity, quantityToRemove);
                    var serials = await _context.ProductSerials
                        .Where(serial =>
                            serial.LotId == lot.LotId
                            && serial.VariantId == variantId
                            && serial.Status == "InStock")
                        .OrderBy(serial => serial.CreatedDate)
                        .ThenBy(serial => serial.SerialId)
                        .Take(takeQuantity)
                        .ToListAsync(cancellationToken);

                    if (serials.Count != takeQuantity)
                    {
                        return Json(Fail(
                            $"Lô #{lot.LotId} thiếu serial InStock, không thể kiểm kê giảm an toàn."));
                    }

                    foreach (var serial in serials)
                    {
                        serial.Status = "AdjustedOut";
                        serial.SoldDate = now;
                    }

                    lot.RemainingQuantity -= takeQuantity;
                    quantityToRemove -= takeQuantity;
                }
            }

            variant.Stock = actualStock;
            _context.InventoryTransactions.Add(new InventoryTransactions
            {
                VariantId = variantId,
                TransactionType = "ADJUST",
                Quantity = difference,
                TransactionDate = now,
                AccountId = GetCurrentAccountId(),
                Note =
                    $"Kiểm kê: {note.Trim()}. Tồn theo lô {currentLotStock} -> {actualStock}; "
                    + $"tồn tổng hợp trước sửa: {previousAggregateStock}."
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                newStock = actualStock,
                diff = difference,
                previousLotStock = currentLotStock,
                previousAggregateStock,
                mismatchCorrected = previousAggregateStock != currentLotStock
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "AdjustStock business correction failed.");
            return Json(Fail("Không thể kiểm kê: " + exception.Message));
        }
    }

    [HttpPost("DeleteLotPhysical", Order = -100)]
    public async Task<IActionResult> DeleteLotPhysical(
        [FromBody] InventoryDeleteLotRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null
            || request.LotId <= 0
            || request.VariantId <= 0
            || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Json(Fail("Thông tin xóa lô hoặc lý do không hợp lệ."));
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            var lot = await _context.InventoryLots
                .FirstOrDefaultAsync(
                    item => item.LotId == request.LotId,
                    cancellationToken);
            if (lot == null || lot.VariantId != request.VariantId)
            {
                return Json(Fail("Lô hàng không tồn tại hoặc không thuộc biến thể đã gửi."));
            }

            if (lot.IsDeleted)
            {
                return Json(Fail("Lô hàng đã được xóa trước đó."));
            }

            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(
                    item => item.VariantId == lot.VariantId,
                    cancellationToken);
            if (variant == null)
            {
                return Json(Fail("Không tìm thấy biến thể của lô hàng."));
            }

            int removedQuantity = Math.Max(0, lot.RemainingQuantity);
            var inStockSerials = await _context.ProductSerials
                .Where(serial =>
                    serial.LotId == lot.LotId
                    && serial.Status == "InStock")
                .ToListAsync(cancellationToken);
            foreach (var serial in inStockSerials)
            {
                serial.Status = "Deleted";
            }

            lot.IsDeleted = true;
            lot.IsActive = false;

            var otherLots = await _context.InventoryLots
                .Where(item =>
                    item.VariantId == lot.VariantId
                    && item.LotId != lot.LotId
                    && item.IsActive
                    && !item.IsDeleted)
                .ToListAsync(cancellationToken);
            int newStock = otherLots.Sum(item => Math.Max(0, item.RemainingQuantity));
            int oldStock = Math.Max(0, variant.Stock ?? 0);
            variant.Stock = newStock;

            _context.InventoryTransactions.Add(new InventoryTransactions
            {
                VariantId = lot.VariantId,
                TransactionType = "LOT_SOFT_DELETE",
                Quantity = -removedQuantity,
                TransactionDate = DateTime.Now,
                AccountId = GetCurrentAccountId(),
                Note =
                    $"Xóa mềm lô #{lot.LotId}: {request.Reason.Trim()}. Tồn tổng {oldStock} -> {newStock}."
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                message = $"Đã xóa mềm lô #{lot.LotId} và loại {removedQuantity} sản phẩm khả dụng khỏi tồn kho."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "DeleteLotPhysical business correction failed.");
            return Json(Fail("Không thể xóa lô: " + exception.Message));
        }
    }

    [HttpPost("RestoreLot", Order = -100)]
    public async Task<IActionResult> RestoreLot(
        [FromBody] InventoryRestoreLotRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null
            || request.LotId <= 0
            || request.VariantId <= 0
            || string.IsNullOrWhiteSpace(request.Reason))
        {
            return Json(Fail("Thông tin khôi phục lô hoặc lý do không hợp lệ."));
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            var lot = await _context.InventoryLots
                .FirstOrDefaultAsync(
                    item => item.LotId == request.LotId,
                    cancellationToken);
            if (lot == null || lot.VariantId != request.VariantId)
            {
                return Json(Fail("Lô hàng không tồn tại hoặc không thuộc biến thể đã gửi."));
            }

            if (!lot.IsDeleted)
            {
                return Json(Fail("Lô hàng chưa bị xóa."));
            }

            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(
                    item => item.VariantId == lot.VariantId,
                    cancellationToken);
            if (variant == null)
            {
                return Json(Fail("Không tìm thấy biến thể của lô hàng."));
            }

            var deletedSerials = await _context.ProductSerials
                .Where(serial =>
                    serial.LotId == lot.LotId
                    && serial.Status == "Deleted")
                .ToListAsync(cancellationToken);
            foreach (var serial in deletedSerials)
            {
                serial.Status = "InStock";
            }

            lot.IsDeleted = false;
            lot.IsActive = true;

            var allLots = await _context.InventoryLots
                .Where(item =>
                    item.VariantId == lot.VariantId
                    && !item.IsDeleted)
                .ToListAsync(cancellationToken);
            int newStock = allLots
                .Where(item => item.IsActive)
                .Sum(item => Math.Max(0, item.RemainingQuantity));
            int oldStock = Math.Max(0, variant.Stock ?? 0);
            variant.Stock = newStock;

            _context.InventoryTransactions.Add(new InventoryTransactions
            {
                VariantId = lot.VariantId,
                TransactionType = "LOT_RESTORE",
                Quantity = Math.Max(0, lot.RemainingQuantity),
                TransactionDate = DateTime.Now,
                AccountId = GetCurrentAccountId(),
                Note =
                    $"Khôi phục lô #{lot.LotId}: {request.Reason.Trim()}. Tồn tổng {oldStock} -> {newStock}."
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                message = $"Đã khôi phục lô #{lot.LotId}."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "RestoreLot business correction failed.");
            return Json(Fail("Không thể khôi phục lô: " + exception.Message));
        }
    }

    [HttpPost("UpdateLotDetail", Order = -100)]
    public async Task<IActionResult> UpdateLotDetail(
        [FromForm] InventoryUpdateLotRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null
            || request.LotId <= 0
            || request.VariantId <= 0)
        {
            return Json(Fail("Thông tin lô hàng không hợp lệ."));
        }

        if (request.RemainingQuantity < 0
            || request.UnitCost < 0m
            || request.Price < 0m)
        {
            return Json(Fail("Số lượng và giá không được âm."));
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            var lot = await _context.InventoryLots
                .FirstOrDefaultAsync(
                    item => item.LotId == request.LotId,
                    cancellationToken);
            if (lot == null || lot.VariantId != request.VariantId)
            {
                return Json(Fail("Lô hàng không tồn tại hoặc không thuộc biến thể đã gửi."));
            }

            if (lot.IsDeleted)
            {
                return Json(Fail("Không thể chỉnh sửa lô đã xóa."));
            }

            if (request.RemainingQuantity > lot.ReceivedQuantity)
            {
                return Json(Fail(
                    "Không được tăng tồn còn lại vượt số lượng đã nhận của lô. Hãy dùng chức năng kiểm kê để tạo lô điều chỉnh riêng."));
            }

            var variant = await _context.ProductVariants
                .Include(item => item.Product)
                .FirstOrDefaultAsync(
                    item => item.VariantId == request.VariantId,
                    cancellationToken);
            if (variant == null)
            {
                return Json(Fail("Không tìm thấy biến thể."));
            }

            int oldRemaining = lot.RemainingQuantity;
            decimal oldCost = lot.UnitCost;
            int difference = request.RemainingQuantity - oldRemaining;

            if (difference < 0)
            {
                int quantityToRemove = Math.Abs(difference);
                var serials = await _context.ProductSerials
                    .Where(serial =>
                        serial.LotId == lot.LotId
                        && serial.Status == "InStock")
                    .OrderBy(serial => serial.CreatedDate)
                    .ThenBy(serial => serial.SerialId)
                    .Take(quantityToRemove)
                    .ToListAsync(cancellationToken);
                if (serials.Count != quantityToRemove)
                {
                    return Json(Fail("Số serial InStock không đủ để giảm tồn lô."));
                }

                foreach (var serial in serials)
                {
                    serial.Status = "AdjustedOut";
                    serial.SoldDate = DateTime.Now;
                }
            }
            else if (difference > 0)
            {
                return Json(Fail(
                    "Tăng tồn trực tiếp trên lô cũ bị chặn để bảo toàn lịch sử nhập. Hãy dùng chức năng kiểm kê."));
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
                string folderPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads",
                    "products");
                Directory.CreateDirectory(folderPath);
                string fileName =
                    $"{Guid.NewGuid():N}{Path.GetExtension(request.ImageFile.FileName)}";
                string fullPath = Path.Combine(folderPath, fileName);
                await using var stream = new FileStream(
                    fullPath,
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

            var lots = await _context.InventoryLots
                .Where(item =>
                    item.VariantId == variant.VariantId
                    && item.IsActive
                    && !item.IsDeleted)
                .ToListAsync(cancellationToken);
            int oldAggregateStock = Math.Max(0, variant.Stock ?? 0);
            int newAggregateStock = lots.Sum(item => Math.Max(0, item.RemainingQuantity));
            variant.Stock = newAggregateStock;

            _context.InventoryTransactions.Add(new InventoryTransactions
            {
                VariantId = variant.VariantId,
                TransactionType = "LOT_CORRECTION",
                Quantity = difference,
                TransactionDate = DateTime.Now,
                AccountId = GetCurrentAccountId(),
                Note =
                    $"Sửa lô #{lot.LotId}: tồn {oldRemaining}->{lot.RemainingQuantity}; "
                    + $"vốn {oldCost:N2}->{lot.UnitCost:N2}; tồn tổng {oldAggregateStock}->{newAggregateStock}. "
                    + "Vị trí kho không được ghi đè vào Poid."
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                message =
                    "Đã cập nhật lô. Vị trí kho chưa được thay đổi vì schema hiện tại chưa có WarehouseId trên InventoryLots."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "UpdateLotDetail business correction failed.");
            return Json(Fail("Không thể cập nhật lô: " + exception.Message));
        }
    }

    [HttpGet("FinancialOverview", Order = -100)]
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
            .Select(variant => new
            {
                variant.VariantId,
                Stock = variant.Stock ?? 0
            })
            .ToListAsync(cancellationToken);
        int mismatchCount = variantStocks.Count(variant =>
            variant.Stock
            != lotStockByVariant.GetValueOrDefault(variant.VariantId));

        var allCompletedSales = await _context.SalesOrders
            .AsNoTracking()
            .Where(order =>
                order.Status == "Completed"
                && order.OrderDate >= start
                && order.OrderDate < endExclusive)
            .ToListAsync(cancellationToken);

        // Chỉ cộng các phiếu đã được ghi theo công thức mới để tránh trộn
        // dữ liệu lịch sử từng xem VAT và shipping cost là lợi nhuận.
        var completedSales = allCompletedSales
            .Where(order =>
                order.Notes != null
                && order.Notes.Contains(
                    "ProfitTotal = doanh thu thuần chưa VAT",
                    StringComparison.Ordinal))
            .ToList();
        int legacyCompletedCount = allCompletedSales.Count - completedSales.Count;

        decimal invoiceTotal = completedSales.Sum(order => order.TotalAmount);
        decimal outputVat = completedSales.Sum(order => order.TaxAmount);
        decimal netRevenue = invoiceTotal - outputVat;
        decimal cogs = completedSales.Sum(order => order.COGSTotal);
        decimal shipping = completedSales.Sum(order => order.ShippingCost);
        decimal recognizedProfit = completedSales.Sum(order => order.ProfitTotal);

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
            legacyCompletedCount,
            invoiceTotal = InventoryFinancialCalculator.RoundMoney(invoiceTotal),
            netRevenueBeforeVat = InventoryFinancialCalculator.RoundMoney(netRevenue),
            outputVat = InventoryFinancialCalculator.RoundMoney(outputVat),
            cogs = InventoryFinancialCalculator.RoundMoney(cogs),
            shippingCost = InventoryFinancialCalculator.RoundMoney(shipping),
            recognizedProfit = InventoryFinancialCalculator.RoundMoney(recognizedProfit),
            stockMismatchCount = mismatchCount,
            basis =
                "Lợi nhuận đã ghi nhận của phiếu xuất Completed = doanh thu thuần chưa VAT - FIFO COGS - chi phí vận chuyển. Chưa đồng nghĩa tiền mặt đã thu nếu chưa có công nợ."
        });
    }

    private async Task<SalesPlan> BuildSalesPlanAsync(
        InventorySoRequest request,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var normalizedItems = NormalizeSalesItems(request.Items);
        var variantIds = normalizedItems
            .Select(item => item.VariantId)
            .ToList();

        IQueryable<ProductVariants> variantQuery = _context.ProductVariants
            .Include(variant => variant.Product)
            .Where(variant => variantIds.Contains(variant.VariantId));
        if (!tracking)
        {
            variantQuery = variantQuery.AsNoTracking();
        }

        var variants = await variantQuery.ToDictionaryAsync(
            variant => variant.VariantId,
            cancellationToken);
        if (variants.Count != variantIds.Count)
        {
            throw new InvalidOperationException("Có biến thể không tồn tại.");
        }

        var allLotsByVariant = new Dictionary<int, List<InventoryLots>>();
        var allocations = new List<FifoAllocation>();
        decimal totalCogs = 0m;

        foreach (var item in normalizedItems)
        {
            IQueryable<InventoryLots> lotQuery = _context.InventoryLots
                .Where(lot =>
                    lot.VariantId == item.VariantId
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
            allLotsByVariant[item.VariantId] = lots;
            int available = lots.Sum(lot => lot.RemainingQuantity);
            if (available < item.Quantity)
            {
                throw new InvalidOperationException(
                    $"Biến thể #{item.VariantId} chỉ còn {available} sản phẩm theo lô, cần {item.Quantity}.");
            }

            int remaining = item.Quantity;
            decimal netRevenueAllocated = 0m;
            for (int lotIndex = 0; lotIndex < lots.Count && remaining > 0; lotIndex++)
            {
                var lot = lots[lotIndex];
                int quantity = Math.Min(lot.RemainingQuantity, remaining);
                remaining -= quantity;
                bool isLastForVariant = remaining == 0;
                allocations.Add(new FifoAllocation(
                    item.VariantId,
                    variants[item.VariantId],
                    lot,
                    quantity,
                    item.ExportPrice,
                    InventoryFinancialCalculator.NormalizeTaxRate(item.TaxRate),
                    isLastForVariant,
                    netRevenueAllocated));

                totalCogs += quantity * lot.UnitCost;
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

        // Ghi lại phần doanh thu đã phân bổ trước mỗi allocation để allocation cuối nhận phần dư làm tròn.
        var rebuiltAllocations = new List<FifoAllocation>();
        foreach (var item in normalizedItems)
        {
            decimal allocated = 0m;
            var itemAllocations = allocations
                .Where(allocation => allocation.VariantId == item.VariantId)
                .ToList();
            var line = financial.Lines.Single(value => value.VariantId == item.VariantId);

            for (int index = 0; index < itemAllocations.Count; index++)
            {
                var allocation = itemAllocations[index];
                rebuiltAllocations.Add(allocation with
                {
                    IsLastForVariant = index == itemAllocations.Count - 1,
                    NetRevenueAlreadyAllocated = allocated
                });

                if (index < itemAllocations.Count - 1)
                {
                    allocated += InventoryFinancialCalculator.RoundMoney(
                        line.NetBeforeTax
                        * allocation.Quantity
                        / line.Quantity);
                }
            }
        }

        return new SalesPlan(
            financial,
            rebuiltAllocations,
            allLotsByVariant);
    }

    private static List<NormalizedSalesItem> NormalizeSalesItems(
        IReadOnlyCollection<InventorySoItemRequest> items)
    {
        var normalized = new List<NormalizedSalesItem>();
        foreach (var group in items.GroupBy(item => item.VariantId))
        {
            var first = group.First();
            if (group.Any(item =>
                    item.ExportPrice != first.ExportPrice
                    || item.TaxRate != first.TaxRate))
            {
                throw new InvalidOperationException(
                    $"Biến thể #{group.Key} có nhiều dòng với giá hoặc thuế suất khác nhau.");
            }

            normalized.Add(new NormalizedSalesItem(
                group.Key,
                group.Sum(item => item.Quantity),
                first.ExportPrice,
                InventoryFinancialCalculator.NormalizeTaxRate(first.TaxRate)));
        }

        return normalized;
    }

    private static object? ValidateSalesRequest(InventorySoRequest? request)
    {
        if (request == null || request.Items.Count == 0)
        {
            return Fail("Phiếu xuất chưa có mặt hàng hợp lệ.");
        }

        if (request.StoreId <= 0 || request.FromWarehouseId <= 0)
        {
            return Fail("Cửa hàng nhận hoặc kho nguồn không hợp lệ.");
        }

        if (request.DiscountPercent < 0m || request.DiscountPercent > 100m)
        {
            return Fail("Chiết khấu phải nằm trong khoảng 0 đến 100%.");
        }

        if (request.ShippingCost < 0m)
        {
            return Fail("Chi phí vận chuyển không được âm.");
        }

        if (request.Items.Any(item =>
                item.VariantId <= 0
                || item.Quantity <= 0
                || item.ExportPrice <= 0m))
        {
            return Fail("Mỗi dòng xuất phải có biến thể, số lượng và giá hợp lệ.");
        }

        try
        {
            foreach (var item in request.Items)
            {
                InventoryFinancialCalculator.NormalizeTaxRate(item.TaxRate);
            }
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }

        return null;
    }

    private static string BuildSalesOrderNote(string? notes)
    {
        string accountingNote =
            "ProfitTotal = doanh thu thuần chưa VAT - FIFO COGS - chi phí vận chuyển. "
            + "Không xem VAT đầu ra là lợi nhuận.";
        return string.IsNullOrWhiteSpace(notes)
            ? accountingNote
            : notes.Trim() + " | " + accountingNote;
    }

    private int GetCurrentAccountId()
    {
        string? raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out int accountId) && accountId > 0
            ? accountId
            : 1;
    }

    private static object Fail(string message) => new
    {
        success = false,
        message
    };

    private sealed record NormalizedSalesItem(
        int VariantId,
        int Quantity,
        decimal ExportPrice,
        decimal TaxRate);

    private sealed record FifoAllocation(
        int VariantId,
        ProductVariants Variant,
        InventoryLots Lot,
        int Quantity,
        decimal UnitPrice,
        decimal TaxRate,
        bool IsLastForVariant,
        decimal NetRevenueAlreadyAllocated);

    private sealed record SalesPlan(
        SalesFinancialSummary Financial,
        List<FifoAllocation> Allocations,
        Dictionary<int, List<InventoryLots>> AllLotsByVariant);
}

public sealed class InventoryPoItemRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal ImportPrice { get; set; }
    public decimal TaxRate { get; set; }
}

public sealed class InventoryPoRequest
{
    public int SupplierId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal OtherFee { get; set; }
    public bool InputVatDeductible { get; set; } = true;
    public string? Note { get; set; }
    public List<InventoryPoItemRequest> Items { get; set; } = new();
}

public sealed class InventorySoItemRequest
{
    public int VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal ExportPrice { get; set; }
    public decimal TaxRate { get; set; }
}

public sealed class InventorySoRequest
{
    public int StoreId { get; set; }
    public int FromWarehouseId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal ShippingCost { get; set; }
    public string? Notes { get; set; }
    public List<InventorySoItemRequest> Items { get; set; } = new();
}

public sealed class InventoryDeleteLotRequest
{
    public int LotId { get; set; }
    public int VariantId { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class InventoryRestoreLotRequest
{
    public int LotId { get; set; }
    public int VariantId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class InventoryUpdateLotRequest
{
    public int LotId { get; set; }
    public int VariantId { get; set; }
    public string? ProductName { get; set; }
    public string? VariantName { get; set; }
    public int RemainingQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Price { get; set; }
    public string StoreLocation { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public IFormFile? ImageFile { get; set; }
}
