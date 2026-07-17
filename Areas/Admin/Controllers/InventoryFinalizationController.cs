using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory/PhaseC")]
public sealed class InventoryFinalizationController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<InventoryFinalizationController> _logger;

    public InventoryFinalizationController(
        ApplicationDbContext context,
        ILogger<InventoryFinalizationController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("Audit")]
    public async Task<IActionResult> Audit(
        CancellationToken cancellationToken)
    {
        var report = await InventoryFinalAuditService.AuditAsync(
            _context,
            cancellationToken);

        return Json(new
        {
            success = true,
            report.GeneratedAt,
            report.Healthy,
            report.CriticalIssueCount,
            report.VariantCount,
            report.ActiveLotCount,
            report.AvailableQuantity,
            variantStockMismatchCount =
                report.VariantStockMismatches.Count,
            lotIssueCount = report.LotIssues.Count,
            allocationIssueCount = report.AllocationIssues.Count,
            financialIssueCount = report.FinancialIssues.Count,
            report.DeductedOrdersWithoutAllocation,
            variantStockMismatches =
                report.VariantStockMismatches,
            lotIssues = report.LotIssues,
            allocationIssues = report.AllocationIssues,
            financialIssues = report.FinancialIssues
        });
    }

    [HttpPost("RepairVariantStockSnapshots")]
    public async Task<IActionResult> RepairVariantStockSnapshots(
        [FromBody] InventoryFinalRepairRequest? request,
        CancellationToken cancellationToken)
    {
        string reason = request?.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 5)
        {
            return BadRequest(new
            {
                success = false,
                message = "Cần nhập lý do đối soát tối thiểu 5 ký tự."
            });
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var result = await InventoryFinalAuditService
                .RepairVariantStockSnapshotsAsync(
                    _context,
                    GetCurrentAccountId(),
                    reason,
                    cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return Json(new
            {
                success = true,
                result.RepairedVariantCount,
                result.ScannedVariantCount,
                message =
                    $"Đã đồng bộ {result.RepairedVariantCount} biến thể theo tổng tồn lô."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                exception,
                "Inventory final stock snapshot repair failed.");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message =
                        "Không thể đồng bộ tồn tổng: "
                        + exception.Message
                });
        }
    }

    [HttpGet("FinancialOverview")]
    public async Task<IActionResult> FinancialOverview(
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        DateTime start = fromDate?.Date
            ?? new DateTime(DateTime.Now.Year, 1, 1);
        DateTime endExclusive = toDate?.Date.AddDays(1)
            ?? start.AddYears(1);

        if (endExclusive <= start)
        {
            return BadRequest(new
            {
                success = false,
                message = "Khoảng thời gian báo cáo không hợp lệ."
            });
        }

        var overview = await OrderProfitService.GetOverviewAsync(
            _context,
            start,
            endExclusive,
            cancellationToken);

        return Json(new
        {
            success = true,
            overview.FromInclusive,
            overview.ToExclusive,
            overview.OrderCount,
            overview.PaidOrderCount,
            overview.UnpaidOrderCount,
            overview.CostedOrderCount,
            overview.RecognizedOrderCount,
            overview.ExcludedPendingOrderCount,
            overview.InvoiceTotal,
            overview.PaidAmount,
            overview.RefundAmount,
            overview.NetCashCollected,
            overview.OutstandingAmount,
            overview.ActiveCogs,
            overview.OutputVatCollected,
            shippingCostProxy =
                overview.CarrierShippingExpense,
            overview.RecognizedGrossProfit,
            overview.CashContributionProfit,
            overview.Limitation
        });
    }

    private int? GetCurrentAccountId()
    {
        string? value =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out int accountId)
            && accountId > 0
                ? accountId
                : null;
    }
}

public sealed class InventoryFinalRepairRequest
{
    public string Reason { get; set; } = string.Empty;
}
