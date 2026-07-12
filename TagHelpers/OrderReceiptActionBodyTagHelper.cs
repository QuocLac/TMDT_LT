using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;
using System.Net;
using TMDT_LT.Models;

namespace TMDT_LT.TagHelpers;

[HtmlTargetElement("body")]
public sealed class OrderReceiptActionBodyTagHelper
    : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override int Order => 360;

    public override void Process(
        TagHelperContext context,
        TagHelperOutput output)
    {
        ReceiptRouteMode mode = ResolveMode();

        if (mode == ReceiptRouteMode.None)
        {
            return;
        }

        int orderId = ResolveOrderId();
        if (orderId <= 0)
        {
            return;
        }

        string receiptUrl =
            $"/orders/{orderId}/receipt";
        string label = mode == ReceiptRouteMode.Admin
            ? "Mở chứng từ đơn hàng"
            : "Xem phiếu đơn hàng";

        string markup = $"""
            <link rel="stylesheet"
                  href="/css/shared/order-receipt.css?v=1.0.0" />
            <div class="order-receipt-action"
                 data-order-receipt-action="true"
                 data-mode="{ModeName(mode)}"
                 hidden>
                <a class="order-receipt-action__button"
                   href="{WebUtility.HtmlEncode(receiptUrl)}"
                   target="_blank"
                   rel="noopener">
                    <i class="fas fa-file-invoice"></i>
                    <span>{WebUtility.HtmlEncode(label)}</span>
                </a>
            </div>
            <script src="/js/shared/order-receipt-action.js?v=1.0.0"
                    defer></script>
            """;

        output.PostContent.AppendHtml(markup);
    }

    private ReceiptRouteMode ResolveMode()
    {
        string area =
            ViewContext.RouteData.Values["area"]
                ?.ToString()
            ?? string.Empty;
        string controller =
            ViewContext.RouteData.Values["controller"]
                ?.ToString()
            ?? string.Empty;
        string action =
            ViewContext.RouteData.Values["action"]
                ?.ToString()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(area)
            && controller.Equals(
                "Checkout",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "OrderPlaced",
                StringComparison.OrdinalIgnoreCase))
        {
            return ReceiptRouteMode.OrderPlaced;
        }

        if (string.IsNullOrWhiteSpace(area)
            && controller.Equals(
                "Customer",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "OrderDetail",
                StringComparison.OrdinalIgnoreCase))
        {
            return ReceiptRouteMode.Customer;
        }

        if (area.Equals(
                "Admin",
                StringComparison.OrdinalIgnoreCase)
            && controller.Equals(
                "Order",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "Details",
                StringComparison.OrdinalIgnoreCase))
        {
            return ReceiptRouteMode.Admin;
        }

        return ReceiptRouteMode.None;
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

        string orderIdQuery =
            ViewContext.HttpContext.Request
                .Query["orderId"]
                .ToString();

        if (int.TryParse(
                orderIdQuery,
                out int parsedOrderId))
        {
            return parsedOrderId;
        }

        string idQuery =
            ViewContext.HttpContext.Request
                .Query["id"]
                .ToString();

        return int.TryParse(
            idQuery,
            out int parsedId)
                ? parsedId
                : 0;
    }

    private static string ModeName(
        ReceiptRouteMode mode) =>
        mode switch
        {
            ReceiptRouteMode.Admin => "admin",
            ReceiptRouteMode.OrderPlaced => "order-placed",
            _ => "customer"
        };

    private enum ReceiptRouteMode
    {
        None,
        OrderPlaced,
        Customer,
        Admin
    }
}
