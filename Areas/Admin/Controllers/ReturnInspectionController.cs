using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Services;

namespace TMDT_LT.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public sealed class ReturnInspectionController
    : Controller
{
    private readonly IReturnInspectionService
        _inspectionService;
    private readonly IReturnWorkflowNotificationService
        _notificationService;

    public ReturnInspectionController(
        IReturnInspectionService inspectionService,
        IReturnWorkflowNotificationService
            notificationService)
    {
        _inspectionService = inspectionService;
        _notificationService =
            notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var model =
            await _inspectionService.GetQueueAsync(
                cancellationToken);

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        int returnId,
        CancellationToken cancellationToken)
    {
        var model =
            await _inspectionService.GetDetailsAsync(
                returnId,
                cancellationToken);

        return model == null
            ? NotFound()
            : View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalize(
        ReturnInspectionFinalizeInput input,
        CancellationToken cancellationToken)
    {
        string actor =
            User.Identity?.Name
            ?? "admin";

        ReturnInspectionFinalizeResult result =
            await _inspectionService.FinalizeAsync(
                input,
                actor,
                cancellationToken);

        if (!result.Success)
        {
            TempData["Error"] = result.Message;

            return RedirectToAction(
                nameof(Details),
                new
                {
                    returnId = input.ReturnId
                });
        }

        if (!result.AlreadyFinalized
            && result.Decision
                == TMDT_LT.Models
                    .ReturnInspectionDecisions.Reject
            && !string.IsNullOrWhiteSpace(
                result.CustomerEmail))
        {
            await _notificationService.SendAsync(
                new ReturnWorkflowNotification(
                    ReturnWorkflowNotificationKinds
                        .Rejected,
                    result.OrderId,
                    result.CustomerEmail!,
                    result.CustomerName
                        ?? "Quý khách",
                    input.SummaryNote?.Trim()
                        ?? "Sản phẩm trả về không đủ điều kiện hoàn tiền."),
                cancellationToken);
        }

        TempData[
            result.RequiresReview
                ? "Warning"
                : "Success"
        ] = result.Message;

        return RedirectToAction(
            nameof(Details),
            new
            {
                returnId = input.ReturnId
            });
    }
}
