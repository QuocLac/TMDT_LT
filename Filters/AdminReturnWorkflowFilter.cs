using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Threading.Tasks;
using TMDT_LT.Services;

namespace TMDT_LT.Filters;

public sealed class AdminReturnWorkflowFilter
    : IAsyncAuthorizationFilter,
      IAsyncActionFilter,
      IOrderedFilter
{
    private readonly IReturnWorkflowService
        _returnWorkflowService;
    private readonly IReturnWorkflowNotificationService
        _notificationService;

    public AdminReturnWorkflowFilter(
        IReturnWorkflowService returnWorkflowService,
        IReturnWorkflowNotificationService
            notificationService)
    {
        _returnWorkflowService =
            returnWorkflowService;
        _notificationService =
            notificationService;
    }

    public int Order => -400;

    public Task OnAuthorizationAsync(
        AuthorizationFilterContext context)
    {
        if (!IsAdminReturnController(
                context.RouteData.Values))
        {
            return Task.CompletedTask;
        }

        if (context.HttpContext.User.Identity
                ?.IsAuthenticated != true
            || !context.HttpContext.User
                .IsInRole("Admin"))
        {
            context.Result = new ForbidResult();
        }

        return Task.CompletedTask;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (!IsUpdateReturnStatus(context))
        {
            await next();
            return;
        }

        if (context.Result != null)
        {
            return;
        }

        int returnId = ResolveInt(
            context,
            "returnId");
        string? newStatus = ResolveString(
            context,
            "newStatus");
        string? adminNote = ResolveString(
            context,
            "adminNote");

        string actor =
            context.HttpContext.User.Identity
                ?.Name
            ?? "admin";

        ReturnWorkflowResult result =
            await _returnWorkflowService
                .TransitionAsync(
                    new ReturnWorkflowCommand(
                        returnId,
                        newStatus,
                        adminNote,
                        actor),
                    context.HttpContext
                        .RequestAborted);

        if (result.Success
            && !result.AlreadyApplied
            && result.OrderId.HasValue
            && !string.IsNullOrWhiteSpace(
                result.CustomerEmail)
            && result.NotificationKind
                != ReturnWorkflowNotificationKinds.None)
        {
            await _notificationService.SendAsync(
                new ReturnWorkflowNotification(
                    result.NotificationKind,
                    result.OrderId.Value,
                    result.CustomerEmail!,
                    result.CustomerName
                        ?? "Quý khách",
                    adminNote?.Trim()
                        ?? string.Empty),
                context.HttpContext
                    .RequestAborted);
        }

        context.Result = new JsonResult(new
        {
            success = result.Success,
            alreadyApplied =
                result.AlreadyApplied,
            message = result.Message,
            returnId = result.ReturnId,
            orderId = result.OrderId,
            paymentStateRepaired =
                result.PaymentStateRepaired,
            orderStateRepaired =
                result.OrderStateRepaired
        });
    }

    private static bool IsUpdateReturnStatus(
        ActionExecutingContext context)
    {
        if (!IsAdminReturnController(
                context.RouteData.Values))
        {
            return false;
        }

        string action =
            context.RouteData.Values["action"]
                ?.ToString()
            ?? string.Empty;

        return context.HttpContext.Request.Method
                .Equals(
                    "POST",
                    StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "UpdateReturnStatus",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdminReturnController(
        System.Collections.Generic
            .IDictionary<string, object?> routeValues)
    {
        string area =
            routeValues["area"]
                ?.ToString()
            ?? string.Empty;
        string controller =
            routeValues["controller"]
                ?.ToString()
            ?? string.Empty;

        return area.Equals(
                "Admin",
                StringComparison.OrdinalIgnoreCase)
            && controller.Equals(
                "Return",
                StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveInt(
        ActionExecutingContext context,
        string name)
    {
        if (!context.ActionArguments.TryGetValue(
                name,
                out object? value))
        {
            return 0;
        }

        return value is int typed
            ? typed
            : int.TryParse(
                value?.ToString(),
                out int parsed)
                ? parsed
                : 0;
    }

    private static string? ResolveString(
        ActionExecutingContext context,
        string name) =>
        context.ActionArguments.TryGetValue(
            name,
            out object? value)
                ? value?.ToString()
                : null;
}
