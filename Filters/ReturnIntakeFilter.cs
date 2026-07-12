using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using TMDT_LT.Services;

namespace TMDT_LT.Filters;

public sealed class ReturnIntakeFilter
    : IAsyncActionFilter,
      IOrderedFilter
{
    private readonly IReturnIntakeService
        _returnIntakeService;

    public ReturnIntakeFilter(
        IReturnIntakeService returnIntakeService)
    {
        _returnIntakeService =
            returnIntakeService;
    }

    public int Order => -450;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (!IsSubmitReturn(context))
        {
            await next();
            return;
        }

        int customerId =
            ResolveCustomerId(context);
        int orderId = ResolveIntArgument(
            context,
            "orderId");

        IReadOnlyList<IFormFile> files =
            ResolveFiles(context);

        ReturnIntakeResult result =
            await _returnIntakeService.SubmitAsync(
                new ReturnIntakeCommand(
                    orderId,
                    customerId,
                    ResolveStringArgument(
                        context,
                        "reason"),
                    ResolveStringArgument(
                        context,
                        "description"),
                    ResolveStringArgument(
                        context,
                        "bankCode"),
                    ResolveStringArgument(
                        context,
                        "accountNumber"),
                    ResolveStringArgument(
                        context,
                        "accountName"),
                    files),
                context.HttpContext
                    .RequestAborted);

        context.Result = new JsonResult(new
        {
            success = result.Success,
            alreadyExists =
                result.AlreadyExists,
            message = result.Message,
            returnId = result.ReturnId,
            paymentStateRepaired =
                result.PaymentStateRepaired
        });
    }

    private static bool IsSubmitReturn(
        ActionExecutingContext context)
    {
        string controller =
            context.RouteData.Values[
                "controller"
            ]?.ToString()
            ?? string.Empty;
        string action =
            context.RouteData.Values[
                "action"
            ]?.ToString()
            ?? string.Empty;

        return HttpMethods.IsPost(
                context.HttpContext
                    .Request.Method)
            && controller.Equals(
                "Customer",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "SubmitOrderReturn",
                StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveCustomerId(
        ActionExecutingContext context)
    {
        string? raw =
            context.HttpContext.User
                .FindFirstValue("CustomerId")
            ?? context.HttpContext.User
                .FindFirstValue(
                    ClaimTypes.NameIdentifier);

        return int.TryParse(
            raw,
            out int customerId)
                ? customerId
                : 0;
    }

    private static int ResolveIntArgument(
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

    private static string? ResolveStringArgument(
        ActionExecutingContext context,
        string name)
    {
        return context.ActionArguments.TryGetValue(
                name,
                out object? value)
            ? value?.ToString()
            : null;
    }

    private static IReadOnlyList<IFormFile>
        ResolveFiles(
            ActionExecutingContext context)
    {
        if (!context.ActionArguments.TryGetValue(
                "files",
                out object? value)
            || value == null)
        {
            return Array.Empty<IFormFile>();
        }

        if (value is IReadOnlyList<IFormFile>
            readOnlyFiles)
        {
            return readOnlyFiles;
        }

        if (value is IEnumerable<IFormFile>
            enumerableFiles)
        {
            return new List<IFormFile>(
                enumerableFiles);
        }

        return Array.Empty<IFormFile>();
    }
}
