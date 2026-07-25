using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TMDT_LT.Services.Inventory.Contracts;
using TMDT_LT.Data;
using TMDT_LT.Services.Inventory;

namespace TMDT_LT.Areas.Admin.Controllers;

/// <summary>
/// HTTP endpoints for wholesale/store distribution from a selected warehouse.
/// FIFO planning, availability validation and posting belong to the application service.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/Inventory/Distribution")]
public sealed class InventoryDistributionController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryDistributionService _distributionService;
    private readonly ILogger<InventoryDistributionController> _logger;

    public InventoryDistributionController(
        ApplicationDbContext context,
        IInventoryDistributionService distributionService,
        ILogger<InventoryDistributionController> logger)
    {
        _context = context;
        _distributionService = distributionService;
        _logger = logger;
    }

    [HttpGet("")]
    [HttpGet("~/Admin/Inventory/CreateSO")]
    public IActionResult Workspace()
    {
        return Redirect("/Admin/Inventory/Operations?tab=outbound");
    }

    [HttpPost("Preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(
        [FromBody] InventoryDistributionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(Fail("Phiếu phân phối không hợp lệ."));
        }

        try
        {
            InventoryDistributionPreviewResult result =
                await _distributionService.PreviewAsync(
                    request!,
                    cancellationToken);
            return Json(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(Fail(exception.Message));
        }
    }

    [HttpPost("Submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(
        [FromBody] InventoryDistributionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return BadRequest(Fail("Phiếu phân phối không hợp lệ."));
        }

        try
        {
            InventoryDistributionSubmissionResult result =
                await _distributionService.SubmitAsync(
                    request!,
                    GetCurrentAccountId(),
                    cancellationToken);
            return Json(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(Fail(exception.Message));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Inventory distribution submission failed.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                Fail("Không thể ghi nhận phiếu phân phối."));
        }
    }

    [HttpGet("Recent")]
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


    private int GetCurrentAccountId()
    {
        string? value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, out int accountId) ? accountId : 0;
    }

    private static object Fail(string message) => new
    {
        success = false,
        message
    };
}
