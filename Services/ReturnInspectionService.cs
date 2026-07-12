using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class ReturnInspectionService
    : IReturnInspectionService
{
    private readonly ApplicationDbContext _context;
    private readonly IOrderStateService _orderStateService;
    private readonly ILogger<ReturnInspectionService> _logger;

    public ReturnInspectionService(
        ApplicationDbContext context,
        IOrderStateService orderStateService,
        ILogger<ReturnInspectionService> logger)
    {
        _context = context;
        _orderStateService = orderStateService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReturnInspectionQueueItem>>
        GetQueueAsync(
            CancellationToken cancellationToken = default)
    {
        var returns = await _context.OrderReturns
            .AsNoTracking()
            .Include(current => current.Customer)
            .Where(current =>
                current.Status == ReturnStatuses.Inspecting
                || _context.ReturnInspections.Any(
                    inspection =>
                        inspection.ReturnId
                            == current.ReturnId))
            .OrderByDescending(current =>
                current.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        int[] returnIds = returns
            .Select(current => current.ReturnId)
            .ToArray();

        Dictionary<int, ReturnInspections> inspections =
            await _context.ReturnInspections
                .AsNoTracking()
                .Where(current =>
                    returnIds.Contains(current.ReturnId))
                .ToDictionaryAsync(
                    current => current.ReturnId,
                    cancellationToken);

        return returns.Select(current =>
        {
            inspections.TryGetValue(
                current.ReturnId,
                out ReturnInspections? inspection);

            return new ReturnInspectionQueueItem
            {
                ReturnId = current.ReturnId,
                OrderId = current.OrderId,
                CustomerName =
                    current.Customer?.FullName
                    ?? $"Khách hàng #{current.CustomerId}",
                ReturnStatus = current.Status,
                CreatedAt = current.CreatedAt,
                IsFinalized = inspection != null,
                Decision = inspection?.Decision,
                EligibleRefundAmount =
                    inspection?.EligibleRefundAmount
            };
        }).ToArray();
    }

    public async Task<ReturnInspectionDetailsViewModel?>
        GetDetailsAsync(
            int returnId,
            CancellationToken cancellationToken = default)
    {
        OrderReturns? request =
            await _context.OrderReturns
                .AsNoTracking()
                .Include(current => current.Customer)
                    .ThenInclude(customer =>
                        customer!.Account)
                .Include(current => current.Order)
                    .ThenInclude(order =>
                        order!.OrderDetails)
                .FirstOrDefaultAsync(
                    current =>
                        current.ReturnId == returnId,
                    cancellationToken);

        if (request?.Order == null)
        {
            return null;
        }

        ReturnInspections? inspection =
            await _context.ReturnInspections
                .AsNoTracking()
                .Include(current => current.Items)
                .FirstOrDefaultAsync(
                    current =>
                        current.ReturnId == returnId,
                    cancellationToken);

        Dictionary<int, ReturnInspectionItems> existingItems =
            inspection?.Items.ToDictionary(
                current => current.OrderDetailId)
            ?? new Dictionary<
                int,
                ReturnInspectionItems>();

        var lines = request.Order.OrderDetails
            .OrderBy(current =>
                current.OrderDetailId)
            .Select(detail =>
            {
                int expected = Math.Max(
                    0,
                    detail.Quantity ?? 0);
                decimal unitAmount =
                    ResolveEffectiveUnitAmount(detail);

                existingItems.TryGetValue(
                    detail.OrderDetailId,
                    out ReturnInspectionItems? item);

                return new ReturnInspectionLineViewModel
                {
                    OrderDetailId =
                        detail.OrderDetailId,
                    ProductName =
                        ResolveProductName(detail),
                    VariantName =
                        detail.VariantNameSnapshot
                            ?.Trim()
                        ?? string.Empty,
                    VariantCode =
                        string.IsNullOrWhiteSpace(
                            detail.VariantCodeSnapshot)
                            ? $"DETAIL-{detail.OrderDetailId}"
                            : detail.VariantCodeSnapshot
                                .Trim(),
                    ExpectedQuantity = expected,
                    ReceivedQuantity =
                        item?.ReceivedQuantity ?? 0,
                    ApprovedQuantity =
                        item?.ApprovedQuantity ?? 0,
                    ConditionCode =
                        item?.ConditionCode
                        ?? ReturnInspectionConditionCodes
                            .Unknown,
                    Disposition =
                        item?.Disposition
                        ?? ReturnInspectionDispositions
                            .ManualReview,
                    UnitAmount = unitAmount,
                    EligibleAmount =
                        item?.EligibleAmount ?? 0m,
                    Note = item?.Note
                };
            })
            .ToArray();

        return new ReturnInspectionDetailsViewModel
        {
            ReturnId = request.ReturnId,
            OrderId = request.OrderId,
            CustomerName =
                request.Customer?.FullName
                ?? $"Khách hàng #{request.CustomerId}",
            CustomerEmail =
                request.Customer?.Account?.Email
                ?? string.Empty,
            ReturnReason = request.Reason,
            ReturnDescription =
                request.Description,
            ReturnStatus = request.Status,
            OriginalOrderAmount =
                ResolveOrderAmount(request.Order),
            IsFinalized = inspection != null,
            Decision = inspection?.Decision,
            EligibleRefundAmount =
                inspection?.EligibleRefundAmount
                ?? 0m,
            SummaryNote = inspection?.SummaryNote,
            InspectedAt = inspection?.InspectedAt,
            InspectedBy = inspection?.InspectedBy,
            Lines = lines
        };
    }

    public async Task<ReturnInspectionFinalizeResult>
        FinalizeAsync(
            ReturnInspectionFinalizeInput input,
            string actor,
            CancellationToken cancellationToken = default)
    {
        if (input.ReturnId <= 0)
        {
            return Failure(
                input.ReturnId,
                0,
                "Mã hồ sơ kiểm định không hợp lệ.");
        }

        string normalizedActor =
            NormalizeActor(actor);
        string? summaryNote;
        try
        {
            summaryNote = NormalizeOptional(
                input.SummaryNote,
                1000);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(
                input.ReturnId,
                0,
                ex.Message);
        }

        await using var transaction =
            await _context.Database
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

        try
        {
            OrderReturns? request =
                await _context.OrderReturns
                    .Include(current => current.Customer)
                        .ThenInclude(customer =>
                            customer!.Account)
                    .Include(current => current.Order)
                        .ThenInclude(order =>
                            order!.OrderDetails)
                    .Include(current => current.Order)
                        .ThenInclude(order =>
                            order!.OrderHistories)
                    .FirstOrDefaultAsync(
                        current =>
                            current.ReturnId
                                == input.ReturnId,
                        cancellationToken);

            if (request?.Order == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    input.ReturnId,
                    0,
                    "Không tìm thấy hồ sơ trả hàng.");
            }

            ReturnInspections? existing =
                await _context.ReturnInspections
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        current =>
                            current.ReturnId
                                == input.ReturnId,
                        cancellationToken);

            if (existing != null)
            {
                await transaction.CommitAsync(
                    cancellationToken);

                return new ReturnInspectionFinalizeResult(
                    true,
                    true,
                    existing.Decision
                        == ReturnInspectionDecisions
                            .ManualReview,
                    "Hồ sơ đã được kiểm định trước đó.",
                    request.ReturnId,
                    request.OrderId,
                    existing.Decision,
                    existing.EligibleRefundAmount,
                    request.Customer?.Account?.Email,
                    request.Customer?.FullName);
            }

            if (request.Status
                    != ReturnStatuses.Inspecting
                || request.Order.Status
                    != OrderStatuses.ReturnInspecting)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    request.ReturnId,
                    request.OrderId,
                    "Hồ sơ và đơn hàng phải cùng ở trạng thái Đang kiểm định.");
            }

            bool hasActiveRefundEvent =
                await _context.PaymentTransactions
                    .AsNoTracking()
                    .AnyAsync(
                        current =>
                            current.OrderId
                                == request.OrderId
                            && current.EventType
                                == PaymentEventTypes.Refund
                            && current.IdempotencyKey
                                == $"REFUND:RETURN:{request.ReturnId}"
                            && current.Status
                                != PaymentEventStatuses.Failed
                            && current.Status
                                != PaymentEventStatuses.Rejected,
                        cancellationToken);

            if (hasActiveRefundEvent)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    request.ReturnId,
                    request.OrderId,
                    "Đã có lệnh hoàn tiền hoặc sự kiện cần đối soát. Không thể tạo lại kết quả kiểm định.");
            }

            OrderDetails[] orderDetails =
                request.Order.OrderDetails
                    .OrderBy(current =>
                        current.OrderDetailId)
                    .ToArray();

            if (orderDetails.Length == 0)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    request.ReturnId,
                    request.OrderId,
                    "Đơn hàng không có dòng sản phẩm để kiểm định.");
            }

            Dictionary<int, ReturnInspectionLineInput>
                submittedLines;

            try
            {
                submittedLines =
                    ValidateSubmittedLines(
                        input.Lines,
                        orderDetails);
            }
            catch (InvalidOperationException ex)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    request.ReturnId,
                    request.OrderId,
                    ex.Message);
            }

            DateTime now = DateTime.Now;
            decimal itemEligibleAmount = 0m;
            bool allApproved = true;
            bool anyApproved = false;
            bool requiresManualReview = false;

            var inspection = new ReturnInspections
            {
                ReturnId = request.ReturnId,
                OrderId = request.OrderId,
                Status =
                    ReturnInspectionStatuses.Completed,
                OriginalOrderAmount =
                    ResolveOrderAmount(request.Order),
                SummaryNote = summaryNote,
                CreatedAt = now,
                UpdatedAt = now,
                InspectedAt = now,
                InspectedBy = normalizedActor
            };

            foreach (OrderDetails detail in orderDetails)
            {
                ReturnInspectionLineInput line =
                    submittedLines[
                        detail.OrderDetailId];

                int expected = Math.Max(
                    0,
                    detail.Quantity ?? 0);
                decimal unitAmount =
                    ResolveEffectiveUnitAmount(detail);
                decimal eligibleAmount =
                    decimal.Round(
                        unitAmount
                            * line.ApprovedQuantity,
                        2,
                        MidpointRounding.AwayFromZero);

                itemEligibleAmount += eligibleAmount;

                bool lineFullyApproved =
                    expected > 0
                    && line.ReceivedQuantity
                        == expected
                    && line.ApprovedQuantity
                        == expected
                    && line.Disposition
                        is ReturnInspectionDispositions
                            .Restock
                        or ReturnInspectionDispositions
                            .DamagedStock;

                allApproved &= lineFullyApproved;
                anyApproved |=
                    line.ApprovedQuantity > 0;

                if (line.Disposition
                        == ReturnInspectionDispositions
                            .ManualReview
                    || (line.ApprovedQuantity > 0
                        && line.ApprovedQuantity
                            < expected)
                    || line.ReceivedQuantity
                        < expected)
                {
                    requiresManualReview = true;
                }

                inspection.Items.Add(
                    new ReturnInspectionItems
                    {
                        OrderDetailId =
                            detail.OrderDetailId,
                        ExpectedQuantity = expected,
                        ReceivedQuantity =
                            line.ReceivedQuantity,
                        ApprovedQuantity =
                            line.ApprovedQuantity,
                        ConditionCode =
                            line.ConditionCode!,
                        Disposition =
                            line.Disposition!,
                        UnitAmountSnapshot =
                            unitAmount,
                        EligibleAmount =
                            eligibleAmount,
                        Note = NormalizeOptional(
                            line.Note,
                            500)
                    });
            }

            if (allApproved)
            {
                inspection.Decision =
                    ReturnInspectionDecisions
                        .FullRefund;
                inspection.EligibleRefundAmount =
                    inspection.OriginalOrderAmount;
            }
            else if (!anyApproved
                && !requiresManualReview)
            {
                inspection.Decision =
                    ReturnInspectionDecisions.Reject;
                inspection.EligibleRefundAmount = 0m;
            }
            else
            {
                inspection.Decision =
                    ReturnInspectionDecisions
                        .ManualReview;
                inspection.EligibleRefundAmount =
                    decimal.Round(
                        Math.Max(
                            0m,
                            itemEligibleAmount),
                        2,
                        MidpointRounding.AwayFromZero);
            }

            _context.ReturnInspections.Add(
                inspection);

            string historyNote =
                $"Kiểm định hàng trả hoàn tất. "
                + $"Kết quả: {inspection.Decision}. "
                + $"Giá trị đủ điều kiện: "
                + $"{inspection.EligibleRefundAmount:N0}đ. "
                + $"Người kiểm định: "
                + $"{normalizedActor}.";

            if (inspection.Decision
                == ReturnInspectionDecisions.Reject)
            {
                request.Status =
                    ReturnStatuses.Rejected;
                request.AdminNote =
                    summaryNote
                    ?? "Sản phẩm trả về không đủ điều kiện hoàn tiền.";
                request.ResolvedAt = now;
                request.IsAlertAdminRead = true;

                _orderStateService.Transition(
                    request.Order,
                    OrderStatuses.Completed,
                    historyNote,
                    now);
            }
            else
            {
                _context.OrderHistories.Add(
                    new OrderHistory
                    {
                        OrderId = request.OrderId,
                        Status =
                            OrderStatuses
                                .ReturnInspecting,
                        UpdatedAt = now,
                        Note = historyNote
                    });
            }

            await _context.SaveChangesAsync(
                cancellationToken);
            await transaction.CommitAsync(
                cancellationToken);

            return new ReturnInspectionFinalizeResult(
                true,
                false,
                inspection.Decision
                    == ReturnInspectionDecisions
                        .ManualReview,
                BuildSuccessMessage(
                    inspection.Decision),
                request.ReturnId,
                request.OrderId,
                inspection.Decision,
                inspection.EligibleRefundAmount,
                request.Customer?.Account?.Email,
                request.Customer?.FullName);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return Failure(
                input.ReturnId,
                0,
                ex.Message);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            _logger.LogWarning(
                ex,
                "Xung đột khi chốt kiểm định ReturnId {ReturnId}.",
                input.ReturnId);

            return Failure(
                input.ReturnId,
                0,
                "Hồ sơ vừa được kiểm định bởi tiến trình khác. Vui lòng tải lại trang.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            _logger.LogError(
                ex,
                "Không thể chốt kiểm định ReturnId {ReturnId}.",
                input.ReturnId);

            return Failure(
                input.ReturnId,
                0,
                "Không thể chốt kết quả kiểm định. Vui lòng kiểm tra log hệ thống.");
        }
    }

    private static Dictionary<
        int,
        ReturnInspectionLineInput>
        ValidateSubmittedLines(
            IReadOnlyList<ReturnInspectionLineInput> lines,
            IReadOnlyList<OrderDetails> orderDetails)
    {
        if (lines == null)
        {
            throw new InvalidOperationException(
                "Thiếu dữ liệu kiểm định sản phẩm.");
        }

        int[] expectedIds = orderDetails
            .Select(current =>
                current.OrderDetailId)
            .OrderBy(current => current)
            .ToArray();

        int[] submittedIds = lines
            .Select(current =>
                current.OrderDetailId)
            .OrderBy(current => current)
            .ToArray();

        if (!expectedIds.SequenceEqual(
                submittedIds))
        {
            throw new InvalidOperationException(
                "Danh sách sản phẩm kiểm định không khớp đơn hàng.");
        }

        if (submittedIds.Distinct().Count()
            != submittedIds.Length)
        {
            throw new InvalidOperationException(
                "Một dòng sản phẩm bị gửi kiểm định nhiều lần.");
        }

        Dictionary<int, OrderDetails> detailMap =
            orderDetails.ToDictionary(
                current =>
                    current.OrderDetailId);

        var result =
            new Dictionary<
                int,
                ReturnInspectionLineInput>();

        foreach (ReturnInspectionLineInput line in lines)
        {
            OrderDetails detail =
                detailMap[line.OrderDetailId];
            int expected = Math.Max(
                0,
                detail.Quantity ?? 0);

            if (expected <= 0)
            {
                throw new InvalidOperationException(
                    $"Dòng sản phẩm #{line.OrderDetailId} có số lượng không hợp lệ.");
            }

            if (line.ReceivedQuantity < 0
                || line.ReceivedQuantity > expected)
            {
                throw new InvalidOperationException(
                    $"Số lượng nhận của dòng #{line.OrderDetailId} không hợp lệ.");
            }

            if (line.ApprovedQuantity < 0
                || line.ApprovedQuantity
                    > line.ReceivedQuantity)
            {
                throw new InvalidOperationException(
                    $"Số lượng được duyệt của dòng #{line.OrderDetailId} không hợp lệ.");
            }

            string condition =
                line.ConditionCode?.Trim()
                ?? string.Empty;
            string disposition =
                line.Disposition?.Trim()
                ?? string.Empty;

            if (!ReturnInspectionConditionCodes
                    .IsKnown(condition))
            {
                throw new InvalidOperationException(
                    $"Vui lòng chọn tình trạng cho dòng #{line.OrderDetailId}.");
            }

            if (!ReturnInspectionDispositions
                    .IsKnown(disposition))
            {
                throw new InvalidOperationException(
                    $"Vui lòng chọn hướng xử lý cho dòng #{line.OrderDetailId}.");
            }

            if (disposition
                    == ReturnInspectionDispositions
                        .Reject
                && line.ApprovedQuantity > 0)
            {
                throw new InvalidOperationException(
                    $"Dòng #{line.OrderDetailId} bị từ chối nhưng vẫn có số lượng được duyệt.");
            }

            line.ConditionCode = condition;
            line.Disposition = disposition;

            result.Add(
                line.OrderDetailId,
                line);
        }

        return result;
    }

    private static decimal ResolveEffectiveUnitAmount(
        OrderDetails detail)
    {
        int quantity = Math.Max(
            0,
            detail.Quantity ?? 0);
        decimal lineTotal = Math.Max(
            0m,
            detail.LineTotal ?? 0m);

        if (quantity > 0
            && lineTotal > 0m)
        {
            return decimal.Round(
                lineTotal / quantity,
                2,
                MidpointRounding.AwayFromZero);
        }

        return decimal.Round(
            Math.Max(
                0m,
                detail.UnitPrice ?? 0m),
            2,
            MidpointRounding.AwayFromZero);
    }

    private static decimal ResolveOrderAmount(
        Orders order)
    {
        decimal amount =
            order.GrandTotalAmount > 0m
                ? order.GrandTotalAmount
                : order.TotalAmount ?? 0m;

        return decimal.Round(
            Math.Max(0m, amount),
            2,
            MidpointRounding.AwayFromZero);
    }

    private static string ResolveProductName(
        OrderDetails detail)
    {
        if (!string.IsNullOrWhiteSpace(
                detail.ProductNameSnapshot))
        {
            return detail.ProductNameSnapshot.Trim();
        }

        return $"Sản phẩm dòng #{detail.OrderDetailId}";
    }

    private static string NormalizeActor(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "admin";
        }

        string normalized = value.Trim();

        return normalized.Length <= 120
            ? normalized
            : normalized[..120];
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException(
                $"Nội dung vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string BuildSuccessMessage(
        string decision) =>
        decision switch
        {
            ReturnInspectionDecisions.FullRefund =>
                "Kiểm định hoàn tất: toàn bộ đơn đủ điều kiện hoàn tiền.",
            ReturnInspectionDecisions.Reject =>
                "Kiểm định hoàn tất: hồ sơ bị từ chối và đơn được đóng lại.",
            _ =>
                "Kiểm định hoàn tất: kết quả một phần cần đối soát thủ công, không tự gọi provider."
        };

    private static ReturnInspectionFinalizeResult Failure(
        int returnId,
        int orderId,
        string message) =>
        new(
            false,
            false,
            false,
            message,
            returnId,
            orderId);
}
