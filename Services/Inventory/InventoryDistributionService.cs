using Microsoft.EntityFrameworkCore;
using System.Data;
using TMDT_LT.Services.Inventory.Contracts;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Services.Inventory;

/// <summary>
/// Application service for wholesale/store distribution from a physical
/// warehouse. It owns FIFO planning, COGS snapshots, serial dispatch and
/// distribution-order posting. Controllers must not call other controllers.
/// </summary>
public interface IInventoryDistributionService
{
    Task<InventoryDistributionPreviewResult> PreviewAsync(
        InventoryDistributionRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryDistributionSubmissionResult> SubmitAsync(
        InventoryDistributionRequest request,
        int accountId,
        CancellationToken cancellationToken = default);
}

public sealed class InventoryDistributionService : IInventoryDistributionService
{
    private readonly ApplicationDbContext _context;

    public InventoryDistributionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryDistributionPreviewResult> PreviewAsync(
        InventoryDistributionRequest request,
        CancellationToken cancellationToken = default)
    {
        await ValidateMasterDataAsync(request, cancellationToken);
        await EnsureOperationalAvailabilityAsync(
            request,
            cancellationToken);

        WarehouseDistributionPlan plan = await BuildPlanAsync(
            request,
            tracking: false,
            cancellationToken);

        return new InventoryDistributionPreviewResult
        {
            Success = true,
            WarehouseId = request.FromWarehouseId,
            GrossSalesBeforeTax = plan.Financial.GrossSalesBeforeTax,
            DiscountAmount = plan.Financial.DiscountAmount,
            NetRevenue = plan.Financial.NetSalesBeforeTax,
            TaxAmount = plan.Financial.OutputVatAmount,
            InvoiceTotal = plan.Financial.InvoiceTotal,
            Cogs = plan.Financial.Cogs,
            ShippingCost = plan.Financial.ShippingCost,
            RealizedProfit = plan.Financial.RealizedAccountingProfit,
            MarginPercent = plan.Financial.MarginPercent,
            AccountingBasis =
                "Doanh thu thuần chưa VAT - FIFO COGS của kho xuất - chi phí vận chuyển."
        };
    }

    public async Task<InventoryDistributionSubmissionResult> SubmitAsync(
        InventoryDistributionRequest request,
        int accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0)
        {
            throw new InvalidOperationException(
                "Không xác định được tài khoản quản trị đang ghi nhận phiếu phân phối.");
        }

        await ValidateMasterDataAsync(request, cancellationToken);

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await EnsureOperationalAvailabilityAsync(
                request,
                cancellationToken);

            WarehouseDistributionPlan plan = await BuildPlanAsync(
                request,
                tracking: true,
                cancellationToken);

            DateTime now = DateTime.Now;
            string code =
                $"SO-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            var salesOrder = new SalesOrders
            {
                SOCode = code,
                StoreId = request.StoreId,
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
                AccountId = accountId,
                Notes = BuildSalesOrderNote(request.Notes)
            };

            _context.SalesOrders.Add(salesOrder);
            await _context.SaveChangesAsync(cancellationToken);

            var createdDetails = new List<SalesOrderDetails>();
            decimal remainingShipping = plan.Financial.ShippingCost;
            decimal totalNetRevenue = plan.Financial.NetSalesBeforeTax;
            int allocationIndex = 0;

            foreach (WarehouseFifoDistributionAllocation allocation in plan.Allocations)
            {
                allocationIndex++;

                SalesFinancialLine line = plan.Financial.Lines.Single(item =>
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

                int quantityBefore = allocation.Lot.RemainingQuantity;
                int quantityAfter = quantityBefore - allocation.Quantity;

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

                foreach (ProductSerials serial in serials)
                {
                    serial.Status = "Dispatched";
                    serial.SoldDate = now;
                }

                _context.InventoryTransactions.Add(new InventoryTransactions
                {
                    VariantId = allocation.VariantId,
                    TransactionType = "OUT_DISTRIBUTION_FIFO",
                    Quantity = -allocation.Quantity,
                    ReferenceId = salesOrder.SOId,
                    ReferenceType = "DistributionOrder",
                    TransactionDate = now,
                    AccountId = accountId,
                    WarehouseId = request.FromWarehouseId,
                    LotId = allocation.Lot.LotId,
                    QuantityBefore = quantityBefore,
                    QuantityAfter = quantityAfter,
                    ReasonCode = "DISTRIBUTION",
                    UnitCostSnapshot = allocation.Lot.UnitCost,
                    TotalCostSnapshot = totalCost,
                    ValueImpact = -totalCost,
                    Note =
                        $"Xuất phân phối {salesOrder.SOCode}; lô #{allocation.Lot.LotId}; "
                        + $"lợi nhuận dòng {profit:N2} đ."
                });
            }

            _context.SalesOrderDetails.AddRange(createdDetails);

            foreach (int variantId in plan.Allocations
                         .Select(item => item.VariantId)
                         .Distinct())
            {
                ProductVariants variant = plan.Variants[variantId];
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
                    $"Đối soát lợi nhuận chi tiết lệch "
                    + $"{detailProfit - salesOrder.ProfitTotal:N2} đ.");
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new InventoryDistributionSubmissionResult
            {
                Success = true,
                SoId = salesOrder.SOId,
                SoCode = salesOrder.SOCode,
                WarehouseId = request.FromWarehouseId,
                Profit = salesOrder.ProfitTotal,
                RealizedProfit = salesOrder.ProfitTotal,
                NetRevenue = plan.Financial.NetSalesBeforeTax,
                TaxAmount = plan.Financial.OutputVatAmount,
                InvoiceTotal = plan.Financial.InvoiceTotal,
                Cogs = plan.Financial.Cogs,
                ShippingCost = plan.Financial.ShippingCost,
                MarginPercent = plan.Financial.MarginPercent,
                WarehouseScopeApplied = true
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task EnsureOperationalAvailabilityAsync(
        InventoryDistributionRequest request,
        CancellationToken cancellationToken)
    {
        Warehouses warehouse = await _context.Warehouses
            .AsNoTracking()
            .FirstAsync(
                item =>
                    item.WarehouseId == request.FromWarehouseId
                    && item.IsActive,
                cancellationToken);

        var normalizedItems = request.Items
            .Where(item => item.VariantId > 0 && item.Quantity > 0)
            .GroupBy(item => item.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(item => item.Quantity)
            })
            .ToList();

        List<int> variantIds = normalizedItems
            .Select(item => item.VariantId)
            .ToList();

        bool hasOpenCount = await _context.Set<InventoryCountSession>()
            .AsNoTracking()
            .AnyAsync(
                session =>
                    session.WarehouseId == warehouse.WarehouseId
                    && (session.Status == InventoryCountSessionStatuses.Counting
                        || session.Status == InventoryCountSessionStatuses.PendingApproval)
                    && session.Lines.Any(
                        line => variantIds.Contains(line.VariantId)),
                cancellationToken);

        if (hasOpenCount)
        {
            throw new InvalidOperationException(
                "Có SKU đang nằm trong phiên kiểm kê mở tại kho xuất.");
        }

        Dictionary<int, int> onHand = await _context.InventoryLots
            .AsNoTracking()
            .Where(lot =>
                lot.WarehouseId == warehouse.WarehouseId
                && variantIds.Contains(lot.VariantId)
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

        Dictionary<int, int> reserved = !warehouse.IsPrimary
            ? new Dictionary<int, int>()
            : await _context.Set<OrderReservations>()
                .AsNoTracking()
                .Where(item =>
                    variantIds.Contains(item.VariantId)
                    && item.Status == OrderReservationStatuses.Reserved
                    && (!item.ExpiresAt.HasValue
                        || item.ExpiresAt > DateTime.Now))
                .GroupBy(item => item.VariantId)
                .Select(group => new
                {
                    VariantId = group.Key,
                    Quantity = group.Sum(item => item.Quantity)
                })
                .ToDictionaryAsync(
                    item => item.VariantId,
                    item => item.Quantity,
                    cancellationToken);

        foreach (var line in normalizedItems)
        {
            int available = Math.Max(
                0,
                onHand.GetValueOrDefault(line.VariantId)
                - reserved.GetValueOrDefault(line.VariantId));

            if (available < line.Quantity)
            {
                throw new InvalidOperationException(
                    $"SKU #{line.VariantId} chỉ còn {available} khả dụng "
                    + $"sau reservation, cần {line.Quantity}.");
            }
        }
    }

    private async Task ValidateMasterDataAsync(
        InventoryDistributionRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        bool storeExists = await _context.Stores
            .AsNoTracking()
            .AnyAsync(
                item => item.StoreId == request.StoreId && item.IsActive,
                cancellationToken);

        if (!storeExists)
        {
            throw new InvalidOperationException(
                "Cửa hàng nhận phân phối không tồn tại hoặc đã ngừng hoạt động.");
        }
    }

    private async Task<WarehouseDistributionPlan> BuildPlanAsync(
        InventoryDistributionRequest request,
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
                InventoryDistributionItemRequest first = group.First();

                if (group.Any(item =>
                        item.ExportPrice != first.ExportPrice
                        || item.TaxRate != first.TaxRate))
                {
                    throw new InvalidOperationException(
                        $"Biến thể #{first.VariantId} có nhiều giá hoặc thuế suất khác nhau.");
                }

                return new InventoryDistributionItemRequest
                {
                    VariantId = first.VariantId,
                    Quantity = group.Sum(item => item.Quantity),
                    ExportPrice = first.ExportPrice,
                    TaxRate = first.TaxRate
                };
            })
            .ToList();

        List<int> variantIds = normalizedItems
            .Select(item => item.VariantId)
            .ToList();

        IQueryable<ProductVariants> variantQuery = _context.ProductVariants
            .Where(item => variantIds.Contains(item.VariantId));

        if (!tracking)
        {
            variantQuery = variantQuery.AsNoTracking();
        }

        Dictionary<int, ProductVariants> variants = await variantQuery
            .ToDictionaryAsync(item => item.VariantId, cancellationToken);

        if (variants.Count != variantIds.Count)
        {
            throw new InvalidOperationException(
                "Có biến thể không tồn tại hoặc đã ngừng kinh doanh.");
        }

        var allocations = new List<WarehouseFifoDistributionAllocation>();
        decimal totalCogs = 0m;

        foreach (InventoryDistributionItemRequest item in normalizedItems)
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

            List<InventoryLots> lots = await lotQuery.ToListAsync(cancellationToken);
            int available = lots.Sum(lot => lot.RemainingQuantity);

            if (available < item.Quantity)
            {
                throw new InvalidOperationException(
                    $"Kho #{warehouseId} chỉ còn {available} sản phẩm "
                    + $"của biến thể #{item.VariantId}, cần {item.Quantity}.");
            }

            int remaining = item.Quantity;
            decimal allocatedNetRevenue = 0m;

            foreach (InventoryLots lot in lots)
            {
                if (remaining <= 0)
                {
                    break;
                }

                int quantity = Math.Min(lot.RemainingQuantity, remaining);
                remaining -= quantity;

                allocations.Add(new WarehouseFifoDistributionAllocation(
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

        SalesFinancialSummary financial =
            InventoryFinancialCalculator.CalculateSalesFinancials(
                normalizedItems.Select(item => new SalesLineInput(
                    item.VariantId,
                    item.Quantity,
                    item.ExportPrice,
                    item.TaxRate)).ToList(),
                request.DiscountPercent,
                request.ShippingCost,
                totalCogs);

        return new WarehouseDistributionPlan(
            allocations,
            financial,
            variants);
    }

    private static void ValidateRequest(InventoryDistributionRequest request)
    {
        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException(
                "Phiếu phân phối chưa có mặt hàng hợp lệ.");
        }

        if (request.StoreId <= 0 || request.FromWarehouseId <= 0)
        {
            throw new InvalidOperationException(
                "Cửa hàng nhận và kho xuất là bắt buộc.");
        }

        if (request.DiscountPercent < 0m
            || request.DiscountPercent > 100m
            || request.ShippingCost < 0m)
        {
            throw new InvalidOperationException(
                "Chiết khấu hoặc chi phí vận chuyển không hợp lệ.");
        }

        if (request.Items.Any(item =>
                item.VariantId <= 0
                || item.Quantity <= 0
                || item.ExportPrice <= 0m
                || item.TaxRate < 0m
                || item.TaxRate > 100m))
        {
            throw new InvalidOperationException(
                "Dòng phân phối có SKU, số lượng, giá hoặc thuế suất không hợp lệ.");
        }
    }

    private async Task<int> ResolveWarehouseIdAsync(
        int requestedWarehouseId,
        CancellationToken cancellationToken)
    {
        bool exists = await _context.Warehouses
            .AsNoTracking()
            .AnyAsync(
                item =>
                    item.WarehouseId == requestedWarehouseId
                    && item.IsActive,
                cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException(
                $"Kho #{requestedWarehouseId} không tồn tại hoặc đã ngừng hoạt động.");
        }

        return requestedWarehouseId;
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

    private sealed record WarehouseFifoDistributionAllocation(
        int VariantId,
        ProductVariants Variant,
        InventoryLots Lot,
        int Quantity,
        decimal UnitPrice,
        decimal TaxRate,
        bool IsLastForVariant,
        decimal NetRevenueAlreadyAllocated);

    private sealed record WarehouseDistributionPlan(
        List<WarehouseFifoDistributionAllocation> Allocations,
        SalesFinancialSummary Financial,
        Dictionary<int, ProductVariants> Variants);
}

public sealed class InventoryDistributionPreviewResult
{
    public bool Success { get; init; }
    public int WarehouseId { get; init; }
    public decimal GrossSalesBeforeTax { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal NetRevenue { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal InvoiceTotal { get; init; }
    public decimal Cogs { get; init; }
    public decimal ShippingCost { get; init; }
    public decimal RealizedProfit { get; init; }
    public decimal MarginPercent { get; init; }
    public string AccountingBasis { get; init; } = string.Empty;
}

public sealed class InventoryDistributionSubmissionResult
{
    public bool Success { get; init; }
    public int SoId { get; init; }
    public string SoCode { get; init; } = string.Empty;
    public int WarehouseId { get; init; }
    public decimal Profit { get; init; }
    public decimal RealizedProfit { get; init; }
    public decimal NetRevenue { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal InvoiceTotal { get; init; }
    public decimal Cogs { get; init; }
    public decimal ShippingCost { get; init; }
    public decimal MarginPercent { get; init; }
    public bool WarehouseScopeApplied { get; init; }
}
