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
/// Auditable warehouse counting workflow: snapshot, count, submit, approve,
/// post variances and cancel open sessions.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory/Counts")]
public sealed class InventoryCountingController : Controller
{
    private const int MaxCountLines = 500;

    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryCountingController> _logger;

    public InventoryCountingController(
        ApplicationDbContext context,
        ILogger<InventoryCountingController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Sessions(
        int? warehouseId,
        string? status,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var query = _context.Set<InventoryCountSession>()
            .AsNoTracking()
            .AsQueryable();

        if (warehouseId.HasValue && warehouseId.Value > 0)
        {
            query = query.Where(item => item.WarehouseId == warehouseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            string normalizedStatus = status.Trim();
            query = query.Where(item => item.Status == normalizedStatus);
        }

        var sessions = await query
            .OrderByDescending(item => item.CountSessionId)
            .Take(take)
            .Select(item => new
            {
                countSessionId = item.CountSessionId,
                countCode = item.CountCode,
                warehouseId = item.WarehouseId,
                warehouseCode = item.Warehouse.WarehouseCode,
                warehouseName = item.Warehouse.WarehouseName,
                scopeType = item.ScopeType,
                status = item.Status,
                notes = item.Notes,
                createdAt = item.CreatedAt,
                submittedAt = item.SubmittedAt,
                postedAt = item.PostedAt,
                lineCount = item.Lines.Count,
                countedLineCount = item.Lines.Count(line => line.CountedQuantity.HasValue),
                discrepancyLineCount = item.Lines.Count(line => line.CountedQuantity.HasValue
                    && line.CountedQuantity.Value != line.SystemQuantity),
                varianceValue = item.Lines.Sum(line => (decimal?)line.VarianceValue) ?? 0m
            })
            .ToListAsync(cancellationToken);

        return Json(new { success = true, sessions });
    }

    [HttpGet("{countSessionId:int}")]
    public async Task<IActionResult> Session(
        int countSessionId,
        CancellationToken cancellationToken)
    {
        var session = await _context.Set<InventoryCountSession>()
            .AsNoTracking()
            .Where(item => item.CountSessionId == countSessionId)
            .Select(item => new
            {
                countSessionId = item.CountSessionId,
                countCode = item.CountCode,
                warehouseId = item.WarehouseId,
                warehouseCode = item.Warehouse.WarehouseCode,
                warehouseName = item.Warehouse.WarehouseName,
                scopeType = item.ScopeType,
                status = item.Status,
                notes = item.Notes,
                createdAt = item.CreatedAt,
                submittedAt = item.SubmittedAt,
                postedAt = item.PostedAt,
                cancelledAt = item.CancelledAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            return NotFound(Fail("Không tìm thấy phiên kiểm kê."));
        }

        var lines = await _context.Set<InventoryCountLine>()
            .AsNoTracking()
            .Where(item => item.CountSessionId == countSessionId)
            .OrderBy(item => item.Variant.Product.Name)
            .ThenBy(item => item.VariantId)
            .Select(item => new
            {
                countLineId = item.CountLineId,
                variantId = item.VariantId,
                sku = $"SKU-{item.VariantId:D6}",
                productName = item.Variant.Product.Name,
                color = item.Variant.Color,
                storage = item.Variant.Storage,
                ram = item.Variant.Ram,
                imageUrl = item.Variant.ImageUrl ?? item.Variant.Product.MainImage,
                systemQuantity = item.SystemQuantity,
                countedQuantity = item.CountedQuantity,
                difference = item.Difference,
                unitCostSnapshot = item.UnitCostSnapshot,
                adjustmentUnitCost = item.AdjustmentUnitCost,
                varianceValue = item.VarianceValue,
                reasonCode = item.ReasonCode,
                note = item.Note,
                countedAt = item.CountedAt
            })
            .ToListAsync(cancellationToken);

        return Json(new
        {
            success = true,
            session,
            lines = lines.Select(item => new
            {
                item.countLineId,
                item.variantId,
                item.sku,
                item.productName,
                variantLabel = BuildVariantLabel(item.color, item.storage, item.ram),
                imageUrl = string.IsNullOrWhiteSpace(item.imageUrl)
                    ? "/images/products/default-product.png"
                    : item.imageUrl,
                item.systemQuantity,
                item.countedQuantity,
                item.difference,
                item.unitCostSnapshot,
                item.adjustmentUnitCost,
                item.varianceValue,
                item.reasonCode,
                item.note,
                item.countedAt
            })
        });
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSession(
        [FromBody] CreateInventoryCountRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(Fail("Dữ liệu tạo phiên kiểm kê không hợp lệ."));
        }

        var warehouse = await _context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.WarehouseId == request.WarehouseId && item.IsActive,
                cancellationToken);
        if (warehouse == null)
        {
            return BadRequest(Fail("Kho kiểm kê không tồn tại hoặc đã ngừng hoạt động."));
        }

        string scopeType = string.Equals(request.ScopeType, InventoryCountScopeTypes.Full,
            StringComparison.OrdinalIgnoreCase)
            ? InventoryCountScopeTypes.Full
            : InventoryCountScopeTypes.Cycle;

        List<int> variantIds = request.VariantIds
            .Where(item => item > 0)
            .Distinct()
            .Take(MaxCountLines + 1)
            .ToList();

        if (scopeType == InventoryCountScopeTypes.Full && variantIds.Count == 0)
        {
            variantIds = await _context.InventoryLots
                .AsNoTracking()
                .Where(item => item.WarehouseId == warehouse.WarehouseId
                    && item.RemainingQuantity > 0
                    && item.IsActive
                    && !item.IsDeleted)
                .Select(item => item.VariantId)
                .Distinct()
                .OrderBy(item => item)
                .Take(MaxCountLines + 1)
                .ToListAsync(cancellationToken);
        }

        if (variantIds.Count == 0)
        {
            return BadRequest(Fail("Hãy chọn ít nhất một SKU để kiểm kê."));
        }

        if (variantIds.Count > MaxCountLines)
        {
            return BadRequest(Fail($"Một phiên chỉ hỗ trợ tối đa {MaxCountLines} SKU."));
        }

        var validVariantIds = await _context.ProductVariants
            .AsNoTracking()
            .Where(item => variantIds.Contains(item.VariantId) && item.IsActive == true)
            .Select(item => item.VariantId)
            .ToListAsync(cancellationToken);

        if (validVariantIds.Count != variantIds.Count)
        {
            return BadRequest(Fail("Danh sách có SKU không tồn tại hoặc đã ngừng kinh doanh."));
        }

        bool hasOverlappingOpenSession = await _context.Set<InventoryCountSession>()
            .AsNoTracking()
            .AnyAsync(session => session.WarehouseId == warehouse.WarehouseId
                && (session.Status == InventoryCountSessionStatuses.Counting
                    || session.Status == InventoryCountSessionStatuses.PendingApproval)
                && session.Lines.Any(line => variantIds.Contains(line.VariantId)),
                cancellationToken);

        if (hasOverlappingOpenSession)
        {
            return Conflict(Fail("Một hoặc nhiều SKU đang nằm trong phiên kiểm kê chưa hoàn tất của kho này."));
        }

        var snapshots = await _context.InventoryLots
            .AsNoTracking()
            .Where(item => item.WarehouseId == warehouse.WarehouseId
                && variantIds.Contains(item.VariantId)
                && item.IsActive
                && !item.IsDeleted)
            .GroupBy(item => item.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Quantity = group.Sum(item => item.RemainingQuantity),
                InventoryValue = group.Sum(item => item.RemainingQuantity * item.UnitCost)
            })
            .ToDictionaryAsync(item => item.VariantId, cancellationToken);

        int accountId = GetCurrentAccountId();
        var session = new InventoryCountSession
        {
            CountCode = $"CNT-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8]}".ToUpperInvariant(),
            WarehouseId = warehouse.WarehouseId,
            ScopeType = scopeType,
            Status = InventoryCountSessionStatuses.Counting,
            Notes = NormalizeText(request.Notes, 500),
            CreatedAt = DateTime.Now,
            CreatedByAccountId = accountId > 0 ? accountId : null
        };

        foreach (int variantId in variantIds)
        {
            snapshots.TryGetValue(variantId, out var snapshot);
            int quantity = Math.Max(0, snapshot?.Quantity ?? 0);
            decimal value = Math.Max(0m, snapshot?.InventoryValue ?? 0m);
            session.Lines.Add(new InventoryCountLine
            {
                VariantId = variantId,
                SystemQuantity = quantity,
                UnitCostSnapshot = quantity > 0 ? value / quantity : 0m
            });
        }

        _context.Set<InventoryCountSession>().Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return Json(new
        {
            success = true,
            countSessionId = session.CountSessionId,
            countCode = session.CountCode,
            message = $"Đã tạo phiên {session.CountCode} với {session.Lines.Count} SKU."
        });
    }

    [HttpPost("{countSessionId:int}/lines/{countLineId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCountLine(
        int countSessionId,
        int countLineId,
        [FromBody] SaveInventoryCountLineRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null || request.CountedQuantity < 0)
        {
            return BadRequest(Fail("Số lượng kiểm đếm không hợp lệ."));
        }

        var line = await _context.Set<InventoryCountLine>()
            .Include(item => item.CountSession)
            .FirstOrDefaultAsync(item => item.CountLineId == countLineId
                && item.CountSessionId == countSessionId,
                cancellationToken);

        if (line == null)
        {
            return NotFound(Fail("Không tìm thấy dòng kiểm kê."));
        }

        if (line.CountSession.Status != InventoryCountSessionStatuses.Counting)
        {
            return Conflict(Fail("Chỉ phiên đang kiểm đếm mới được cập nhật số lượng."));
        }

        int difference = request.CountedQuantity - line.SystemQuantity;
        string? reasonCode = NormalizeReasonCode(request.ReasonCode);
        if (difference != 0 && reasonCode == null)
        {
            return BadRequest(Fail("Dòng có chênh lệch bắt buộc phải chọn lý do."));
        }

        decimal? adjustmentUnitCost = request.AdjustmentUnitCost;
        if (difference > 0)
        {
            decimal effectiveCost = adjustmentUnitCost ?? line.UnitCostSnapshot;
            if (effectiveCost <= 0m)
            {
                return BadRequest(Fail("Hàng tăng thêm phải có giá vốn dương để ghi nhận giá trị tồn kho."));
            }
            adjustmentUnitCost = effectiveCost;
        }

        line.CountedQuantity = request.CountedQuantity;
        line.Difference = difference;
        line.ReasonCode = difference == 0 ? null : reasonCode;
        line.Note = NormalizeText(request.Note, 500);
        line.AdjustmentUnitCost = difference > 0 ? adjustmentUnitCost : line.UnitCostSnapshot;
        decimal previewUnitCost = difference > 0
            ? line.AdjustmentUnitCost ?? 0m
            : line.UnitCostSnapshot;
        line.VarianceValue = difference * previewUnitCost;
        line.CountedAt = DateTime.Now;
        int accountId = GetCurrentAccountId();
        line.CountedByAccountId = accountId > 0 ? accountId : null;

        await _context.SaveChangesAsync(cancellationToken);

        return Json(new
        {
            success = true,
            countLineId = line.CountLineId,
            line.CountedQuantity,
            line.Difference,
            line.ReasonCode,
            line.AdjustmentUnitCost,
            message = "Đã lưu kết quả kiểm đếm."
        });
    }

    [HttpPost("{countSessionId:int}/submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitSession(
        int countSessionId,
        CancellationToken cancellationToken)
    {
        var session = await _context.Set<InventoryCountSession>()
            .Include(item => item.Lines)
            .FirstOrDefaultAsync(item => item.CountSessionId == countSessionId, cancellationToken);

        if (session == null)
        {
            return NotFound(Fail("Không tìm thấy phiên kiểm kê."));
        }

        if (session.Status != InventoryCountSessionStatuses.Counting)
        {
            return Conflict(Fail("Phiên này không còn ở trạng thái kiểm đếm."));
        }

        if (session.Lines.Count == 0 || session.Lines.Any(item => !item.CountedQuantity.HasValue))
        {
            return BadRequest(Fail("Cần kiểm đếm và lưu đầy đủ tất cả SKU trước khi gửi duyệt."));
        }

        if (session.Lines.Any(item => item.Difference != 0
            && string.IsNullOrWhiteSpace(item.ReasonCode)))
        {
            return BadRequest(Fail("Mọi dòng chênh lệch phải có lý do."));
        }

        session.Status = InventoryCountSessionStatuses.PendingApproval;
        session.SubmittedAt = DateTime.Now;
        int accountId = GetCurrentAccountId();
        session.SubmittedByAccountId = accountId > 0 ? accountId : null;

        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, message = "Phiên kiểm kê đã được chuyển sang chờ duyệt." });
    }

    [HttpPost("{countSessionId:int}/post")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PostSession(
        int countSessionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var session = await _context.Set<InventoryCountSession>()
                .Include(item => item.Lines)
                .FirstOrDefaultAsync(item => item.CountSessionId == countSessionId,
                    cancellationToken);

            if (session == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return NotFound(Fail("Không tìm thấy phiên kiểm kê."));
            }

            if (session.Status != InventoryCountSessionStatuses.PendingApproval)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(Fail("Chỉ phiên đang chờ duyệt mới được ghi sổ."));
            }

            if (session.Lines.Any(item => !item.CountedQuantity.HasValue))
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(Fail("Phiên còn dòng chưa kiểm đếm."));
            }

            var staleItems = new List<object>();
            foreach (InventoryCountLine line in session.Lines)
            {
                int currentQuantity = await _context.InventoryLots
                    .Where(item => item.WarehouseId == session.WarehouseId
                        && item.VariantId == line.VariantId
                        && item.IsActive
                        && !item.IsDeleted)
                    .SumAsync(item => (int?)item.RemainingQuantity, cancellationToken) ?? 0;

                if (currentQuantity != line.SystemQuantity)
                {
                    staleItems.Add(new
                    {
                        line.VariantId,
                        snapshotQuantity = line.SystemQuantity,
                        currentQuantity
                    });
                }
            }

            if (staleItems.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new
                {
                    success = false,
                    message = "Tồn kho đã thay đổi sau khi tạo phiên. Hãy hủy phiên và kiểm kê lại để tránh ghi đè giao dịch mới.",
                    staleItems
                });
            }

            int accountId = GetCurrentAccountId();
            DateTime postedAt = DateTime.Now;
            var affectedVariantIds = new HashSet<int>();

            foreach (InventoryCountLine line in session.Lines)
            {
                int beforeQuantity = line.SystemQuantity;
                int countedQuantity = line.CountedQuantity!.Value;
                int difference = countedQuantity - beforeQuantity;
                line.Difference = difference;

                if (difference == 0)
                {
                    line.VarianceValue = 0m;
                    continue;
                }

                string reasonCode = NormalizeReasonCode(line.ReasonCode)
                    ?? throw new InvalidOperationException($"SKU #{line.VariantId} thiếu lý do chênh lệch.");

                decimal signedValueImpact;
                decimal effectiveUnitCost;
                int? transactionLotId = null;

                if (difference > 0)
                {
                    effectiveUnitCost = line.AdjustmentUnitCost ?? line.UnitCostSnapshot;
                    if (effectiveUnitCost <= 0m)
                    {
                        throw new InvalidOperationException(
                            $"SKU #{line.VariantId} tăng tồn nhưng chưa có giá vốn hợp lệ.");
                    }

                    var adjustmentLot = new InventoryLots
                    {
                        Poid = null,
                        SupplierId = null,
                        VariantId = line.VariantId,
                        WarehouseId = session.WarehouseId,
                        ReceivedQuantity = difference,
                        RemainingQuantity = difference,
                        UnitCost = effectiveUnitCost,
                        ReceivedDate = postedAt,
                        IsActive = true,
                        IsDeleted = false,
                        SourceType = InventoryLotSourceTypes.CountAdjustment,
                        SourceReference = session.CountCode,
                        InventoryCountLineId = line.CountLineId
                    };
                    _context.InventoryLots.Add(adjustmentLot);
                    await _context.SaveChangesAsync(cancellationToken);

                    for (int serialIndex = 1; serialIndex <= difference; serialIndex++)
                    {
                        _context.ProductSerials.Add(new ProductSerials
                        {
                            VariantId = line.VariantId,
                            LotId = adjustmentLot.LotId,
                            SerialNumber = $"ADJ-{line.VariantId}-{adjustmentLot.LotId}-{serialIndex:D5}-{Guid.NewGuid().ToString("N")[..6]}".ToUpperInvariant(),
                            Status = "InStock",
                            CreatedDate = postedAt
                        });
                    }

                    transactionLotId = adjustmentLot.LotId;
                    signedValueImpact = difference * effectiveUnitCost;
                }
                else
                {
                    int quantityToRemove = Math.Abs(difference);
                    decimal removedValue = 0m;
                    var activeLots = await _context.InventoryLots
                        .Where(item => item.WarehouseId == session.WarehouseId
                            && item.VariantId == line.VariantId
                            && item.RemainingQuantity > 0
                            && item.IsActive
                            && !item.IsDeleted)
                        .OrderBy(item => item.ReceivedDate)
                        .ThenBy(item => item.LotId)
                        .ToListAsync(cancellationToken);

                    foreach (InventoryLots lot in activeLots)
                    {
                        if (quantityToRemove <= 0)
                        {
                            break;
                        }

                        int takeQuantity = Math.Min(lot.RemainingQuantity, quantityToRemove);
                        if (takeQuantity <= 0)
                        {
                            continue;
                        }

                        lot.RemainingQuantity -= takeQuantity;
                        quantityToRemove -= takeQuantity;
                        removedValue += takeQuantity * lot.UnitCost;
                        transactionLotId ??= lot.LotId;

                        var serials = await _context.ProductSerials
                            .Where(item => item.LotId == lot.LotId
                                && item.VariantId == line.VariantId
                                && item.Status == "InStock")
                            .OrderBy(item => item.CreatedDate)
                            .Take(takeQuantity)
                            .ToListAsync(cancellationToken);

                        if (serials.Count != takeQuantity)
                        {
                            throw new InvalidOperationException(
                                $"Lô #{lot.LotId} chỉ có {serials.Count} serial InStock nhưng cần điều chỉnh {takeQuantity}. Hãy đối soát serial trước khi post kiểm kê.");
                        }

                        foreach (ProductSerials serial in serials)
                        {
                            serial.Status = "AdjustedOut";
                        }
                    }

                    if (quantityToRemove > 0)
                    {
                        throw new InvalidOperationException(
                            $"SKU #{line.VariantId} không đủ tồn theo lô để post chênh lệch.");
                    }

                    signedValueImpact = -removedValue;
                    effectiveUnitCost = Math.Abs(difference) > 0
                        ? removedValue / Math.Abs(difference)
                        : 0m;
                }

                line.VarianceValue = signedValueImpact;
                affectedVariantIds.Add(line.VariantId);

                _context.InventoryTransactions.Add(new InventoryTransactions
                {
                    VariantId = line.VariantId,
                    WarehouseId = session.WarehouseId,
                    LotId = transactionLotId,
                    TransactionType = difference > 0 ? "COUNT_GAIN" : "COUNT_LOSS",
                    Quantity = difference,
                    ReferenceId = session.CountSessionId,
                    ReferenceType = "InventoryCount",
                    TransactionDate = postedAt,
                    AccountId = accountId > 0 ? accountId : null,
                    QuantityBefore = beforeQuantity,
                    QuantityAfter = countedQuantity,
                    ReasonCode = reasonCode,
                    UnitCostSnapshot = effectiveUnitCost,
                    TotalCostSnapshot = Math.Abs(signedValueImpact),
                    ValueImpact = signedValueImpact,
                    Note = BuildAuditNote(session.CountCode, reasonCode, line.Note)
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            foreach (int variantId in affectedVariantIds)
            {
                int aggregateOnHand = await _context.InventoryLots
                    .Where(item => item.VariantId == variantId
                        && item.IsActive
                        && !item.IsDeleted)
                    .SumAsync(item => (int?)item.RemainingQuantity, cancellationToken) ?? 0;

                var variant = await _context.ProductVariants
                    .FirstOrDefaultAsync(item => item.VariantId == variantId, cancellationToken);
                if (variant != null)
                {
                    variant.Stock = Math.Max(0, aggregateOnHand);
                    variant.UpdatedDate = postedAt;
                }
            }

            session.Status = InventoryCountSessionStatuses.Posted;
            session.PostedAt = postedAt;
            session.PostedByAccountId = accountId > 0 ? accountId : null;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Json(new
            {
                success = true,
                countSessionId = session.CountSessionId,
                session.CountCode,
                postedAt,
                discrepancyLineCount = session.Lines.Count(item => item.Difference != 0),
                varianceValue = session.Lines.Sum(item => item.VarianceValue),
                message = "Đã ghi sổ chênh lệch kiểm kê và đồng bộ tồn kho."
            });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(exception,
                "Inventory count concurrency conflict for session {CountSessionId}",
                countSessionId);
            return Conflict(Fail("Dữ liệu đã được người khác cập nhật. Hãy tải lại phiên kiểm kê."));
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception,
                "Failed to post inventory count session {CountSessionId}",
                countSessionId);
            return BadRequest(Fail(exception.Message));
        }
    }

    [HttpPost("{countSessionId:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelSession(
        int countSessionId,
        [FromBody] CancelInventoryCountRequest? request,
        CancellationToken cancellationToken)
    {
        var session = await _context.Set<InventoryCountSession>()
            .FirstOrDefaultAsync(item => item.CountSessionId == countSessionId,
                cancellationToken);

        if (session == null)
        {
            return NotFound(Fail("Không tìm thấy phiên kiểm kê."));
        }

        if (session.Status == InventoryCountSessionStatuses.Posted)
        {
            return Conflict(Fail("Phiên đã ghi sổ không thể hủy. Hãy lập phiên điều chỉnh mới."));
        }

        if (session.Status == InventoryCountSessionStatuses.Cancelled)
        {
            return Json(new { success = true, message = "Phiên đã được hủy trước đó." });
        }

        session.Status = InventoryCountSessionStatuses.Cancelled;
        session.CancelledAt = DateTime.Now;
        int accountId = GetCurrentAccountId();
        session.CancelledByAccountId = accountId > 0 ? accountId : null;
        string? reason = NormalizeText(request?.Reason, 300);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            session.Notes = string.IsNullOrWhiteSpace(session.Notes)
                ? $"Hủy: {reason}"
                : $"{session.Notes}\nHủy: {reason}";
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, message = "Đã hủy phiên kiểm kê." });
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

    private int GetCurrentAccountId()
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int accountId)
            ? accountId
            : 0;
    }

    private static string BuildVariantLabel(string? color, string? storage, string? ram)
    {
        string[] parts = new[] { color, storage, ram }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
        return parts.Length == 0 ? "Biến thể mặc định" : string.Join(" / ", parts);
    }

    private static string? NormalizeReasonCode(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return null;
        }

        string normalized = reasonCode.Trim().ToUpperInvariant();
        return InventoryCountReasonCodes.Labels.ContainsKey(normalized)
            ? normalized
            : null;
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string BuildAuditNote(string countCode, string reasonCode, string? note)
    {
        string reason = InventoryCountReasonCodes.Labels.GetValueOrDefault(reasonCode, reasonCode);
        string value = $"[{countCode}] {reason}";
        if (!string.IsNullOrWhiteSpace(note))
        {
            value += $" - {note.Trim()}";
        }
        return value.Length <= 255 ? value : value[..255];
    }

    private static object Fail(string message) => new { success = false, message };
}
