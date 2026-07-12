using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;
using TMDT_LT.Models;

namespace TMDT_LT.TagHelpers;

[HtmlTargetElement("body")]
public sealed class CustomerInvoiceStatusBodyTagHelper
    : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; }
        = null!;

    public override int Order => 410;

    public override void Process(
        TagHelperContext context,
        TagHelperOutput output)
    {
        InvoiceStatusRouteMode mode =
            ResolveMode();

        if (mode == InvoiceStatusRouteMode.None)
        {
            return;
        }

        int orderId = ResolveOrderId();
        if (orderId <= 0)
        {
            return;
        }

        string modeName =
            mode == InvoiceStatusRouteMode.OrderPlaced
                ? "order-placed"
                : "customer";

        output.PostContent.AppendHtml(
            $"""
            <link rel="stylesheet"
                  href="/css/shared/customer-invoice-status.css?v=1.0.0" />
            <div id="customerInvoiceStatusHost"
                 data-customer-invoice-status="true"
                 data-order-id="{orderId}"
                 data-mode="{modeName}"
                 hidden
                 aria-live="polite"></div>
            <script src="/js/shared/customer-invoice-status.js?v=1.0.0"
                    defer></script>
            """);
    }

    private InvoiceStatusRouteMode ResolveMode()
    {
        string area =
            ViewContext.RouteData.Values["area"]
                ?.ToString()
            ?? string.Empty;
        string controller =
            ViewContext.RouteData.Values[
                "controller"
            ]?.ToString()
            ?? string.Empty;
        string action =
            ViewContext.RouteData.Values["action"]
                ?.ToString()
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(area))
        {
            return InvoiceStatusRouteMode.None;
        }

        if (controller.Equals(
                "Checkout",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "OrderPlaced",
                StringComparison.OrdinalIgnoreCase))
        {
            return InvoiceStatusRouteMode.OrderPlaced;
        }

        if (controller.Equals(
                "Customer",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "OrderDetail",
                StringComparison.OrdinalIgnoreCase))
        {
            return InvoiceStatusRouteMode.Customer;
        }

        return InvoiceStatusRouteMode.None;
    }

    private int ResolveOrderId()
    {
        if (ViewContext.ViewData.Model
            is Orders order
            && order.OrderId > 0)
        {
            return order.OrderId;
        }

        object? routeId =
            ViewContext.RouteData.Values["id"];

        if (routeId != null
            && int.TryParse(
                routeId.ToString(),
                out int parsedRouteId))
        {
            return parsedRouteId;
        }

        string queryOrderId =
            ViewContext.HttpContext.Request
                .Query["orderId"]
                .ToString();

        if (int.TryParse(
                queryOrderId,
                out int parsedOrderId))
        {
            return parsedOrderId;
        }

        string queryId =
            ViewContext.HttpContext.Request
                .Query["id"]
                .ToString();

        return int.TryParse(
            queryId,
            out int parsedId)
                ? parsedId
                : 0;
    }

    private enum InvoiceStatusRouteMode
    {
        None,
        OrderPlaced,
        Customer
    }
}
