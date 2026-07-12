using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;
using TMDT_LT.Models;

namespace TMDT_LT.TagHelpers;

[HtmlTargetElement("body")]
public sealed class CustomerRefundTimelineBodyTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override int Order => 250;

    public override void Process(
        TagHelperContext context,
        TagHelperOutput output)
    {
        string area =
            ViewContext.RouteData.Values["area"]?.ToString()
            ?? string.Empty;
        string controller =
            ViewContext.RouteData.Values["controller"]?.ToString()
            ?? string.Empty;
        string action =
            ViewContext.RouteData.Values["action"]?.ToString()
            ?? string.Empty;

        bool isCustomerOrderDetail =
            string.IsNullOrWhiteSpace(area)
            && string.Equals(
                controller,
                "Customer",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                action,
                "OrderDetail",
                StringComparison.OrdinalIgnoreCase);

        if (!isCustomerOrderDetail)
        {
            return;
        }

        int orderId = ResolveOrderId();
        if (orderId <= 0)
        {
            return;
        }

        string markup = $"""
            <link rel="stylesheet"
                  href="/css/shared/customer-refund-timeline.css?v=1.0.0" />
            <div id="customerRefundTimelineHost"
                 data-customer-refund-timeline="true"
                 data-order-id="{orderId}"
                 hidden
                 aria-live="polite"></div>
            <script src="/js/shared/customer-refund-timeline.js?v=1.0.0"
                    defer></script>
            """;

        output.PostContent.AppendHtml(markup);
    }

    private int ResolveOrderId()
    {
        if (ViewContext.ViewData.Model is Orders order
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

        string queryId =
            ViewContext.HttpContext.Request.Query["id"]
                .ToString();

        return int.TryParse(
            queryId,
            out int parsedQueryId)
                ? parsedQueryId
                : 0;
    }
}
