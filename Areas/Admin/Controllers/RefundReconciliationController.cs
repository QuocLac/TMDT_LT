using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMDT_LT.Areas.Admin.ViewModels;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public sealed class RefundReconciliationController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IRefundSettlementService _refundSettlementService;

    public RefundReconciliationController(
        ApplicationDbContext context,
        IRefundSettlementService refundSettlementService)
    {
        _context = context;
        _refundSettlementService = refundSettlementService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string status = PaymentEventStatuses.RequiresReview,
        string scope = "",
        string search = "")
    {
        IQueryable<PaymentTransactions> baseQuery =
            _context.PaymentTransactions
                .AsNoTracking()
                .Where(current =>
                    current.EventType == PaymentEventTypes.Refund);

        int requiresReviewCount = await baseQuery.CountAsync(
            current => current.Status
                == PaymentEventStatuses.RequiresReview,
            HttpContext.RequestAborted);
        int processingCount = await baseQuery.CountAsync(
            current => current.Status
                == PaymentEventStatuses.Processing,
            HttpContext.RequestAborted);
        int failedCount = await baseQuery.CountAsync(
            current => current.Status
                == PaymentEventStatuses.Failed,
            HttpContext.RequestAborted);
        int processedCount = await baseQuery.CountAsync(
            current => current.Status
                == PaymentEventStatuses.Processed,
            HttpContext.RequestAborted);

        IQueryable<PaymentTransactions> query = baseQuery
            .Include(current => current.Payment)
            .Include(current => current.Order)
                .ThenInclude(order => order.OrderReturns)
            .Include(current => current.Order)
                .ThenInclude(order => order.Customer)
                    .ThenInclude(customer => customer!.Account);

        if (!string.IsNullOrWhiteSpace(status)
            && !string.Equals(
                status,
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(current => current.Status == status);
        }

        if (string.Equals(
                scope,
                "return",
                StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(current =>
                current.IdempotencyKey.StartsWith("REFUND:RETURN:"));
        }
        else if (string.Equals(
            scope,
            "cancellation",
            StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(current =>
                current.IdempotencyKey.StartsWith("REFUND:CANCEL:"));
        }

        string normalizedSearch = search?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            string lowered = normalizedSearch.ToLower();
            bool parsedOrderId = int.TryParse(
                normalizedSearch.Replace("#ORD-", string.Empty, StringComparison.OrdinalIgnoreCase),
                out int orderId);

            query = query.Where(current =>
                (parsedOrderId && current.OrderId == orderId)
                || current.IdempotencyKey.ToLower().Contains(lowered)
                || (current.ProviderTransactionId != null
                    && current.ProviderTransactionId.ToLower().Contains(lowered))
                || current.Provider.ToLower().Contains(lowered));
        }

        List<PaymentTransactions> events = await query
            .OrderByDescending(current =>
                current.Status == PaymentEventStatuses.RequiresReview)
            .ThenByDescending(current => current.ReceivedAt)
            .ThenByDescending(current => current.PaymentTransactionId)
            .Take(200)
            .ToListAsync(HttpContext.RequestAborted);

        var items = events.Select(MapItem).ToList();
        var model = new RefundReconciliationIndexViewModel
        {
            StatusFilter = status ?? string.Empty,
            ScopeFilter = scope ?? string.Empty,
            Search = normalizedSearch,
            RequiresReviewCount = requiresReviewCount,
            ProcessingCount = processingCount,
            FailedCount = failedCount,
            ProcessedCount = processedCount,
            Items = items
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(
        long paymentTransactionId,
        string decision,
        bool? isProductIntact,
        string? adminNote,
        string? transactionReference)
    {
        bool providerConfirmedSuccess = string.Equals(
            decision,
            "success",
            StringComparison.OrdinalIgnoreCase);

        if (!providerConfirmedSuccess
            && !string.Equals(
                decision,
                "failed",
                StringComparison.OrdinalIgnoreCase))
        {
            TempData["RefundError"] =
                "Quyết định đối soát không hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        string actor = User.Identity?.Name ?? "admin";
        RefundSettlementResult result =
            await _refundSettlementService.ReconcileRefundAsync(
                new RefundReconciliationCommand(
                    paymentTransactionId,
                    providerConfirmedSuccess,
                    isProductIntact,
                    adminNote,
                    transactionReference,
                    actor),
                HttpContext.RequestAborted);

        TempData[result.Success ? "RefundSuccess" : "RefundError"] =
            result.Message;

        return RedirectToAction(
            nameof(Index),
            new
            {
                status = result.RequiresReview
                    ? PaymentEventStatuses.RequiresReview
                    : "all"
            });
    }

    private static RefundReconciliationItemViewModel MapItem(
        PaymentTransactions current)
    {
        bool isReturn = current.IdempotencyKey.StartsWith(
            "REFUND:RETURN:",
            StringComparison.OrdinalIgnoreCase);
        int? returnId = isReturn
            && int.TryParse(
                current.IdempotencyKey["REFUND:RETURN:".Length..],
                out int parsedReturnId)
                    ? parsedReturnId
                    : null;

        OrderReturns? returnRecord = returnId.HasValue
            ? current.Order.OrderReturns.FirstOrDefault(
                item => item.ReturnId == returnId.Value)
            : null;

        return new RefundReconciliationItemViewModel
        {
            PaymentTransactionId = current.PaymentTransactionId,
            OrderId = current.OrderId,
            PaymentId = current.PaymentId,
            ReturnId = returnId,
            Scope = isReturn ? "Trả hàng" : "Hủy đơn",
            Provider = current.Provider,
            IdempotencyKey = current.IdempotencyKey,
            Amount = current.Amount ?? 0,
            EventStatus = current.Status,
            ResponseCode = current.ResponseCode,
            TransactionStatus = current.TransactionStatus,
            ProviderTransactionId = current.ProviderTransactionId,
            ErrorMessage = current.ErrorMessage,
            ReceivedAt = current.ReceivedAt,
            ProcessedAt = current.ProcessedAt,
            PaymentMethod = current.Payment.PaymentMethod ?? string.Empty,
            PaymentStatus = current.Payment.PaymentStatus ?? string.Empty,
            OrderStatus = current.Order.Status ?? string.Empty,
            ReturnStatus = returnRecord?.Status,
            CustomerName = current.Order.Customer?.FullName,
            CustomerEmail = current.Order.Customer?.Account?.Email,
            CancellationReason = current.Order.CancellationReason,
            CancellationRequestedBy = current.Order.CancellationRequestedBy,
            RequiresManualReference = string.Equals(
                    current.ResponseCode,
                    "MANUAL_REQUIRED",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    current.Provider,
                    PaymentMethods.VnPay,
                    StringComparison.OrdinalIgnoreCase),
            RequiresProductCondition = isReturn,
            CanResolve = current.Status
                != PaymentEventStatuses.Processed
        };
    }
}
