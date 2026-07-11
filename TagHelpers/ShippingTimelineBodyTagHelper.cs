using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;
using TMDT_LT.Models;

namespace TMDT_LT.TagHelpers;

/// <summary>
/// Tự gắn drawer theo dõi vận chuyển vào đúng hai trang chi tiết đơn hàng
/// mà không làm view nghiệp vụ hiện tại phình thêm hoặc bị trùng logic.
/// </summary>
[HtmlTargetElement("body")]
public sealed class ShippingTimelineBodyTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        string area = ViewContext.RouteData.Values["area"]?.ToString() ?? string.Empty;
        string controller = ViewContext.RouteData.Values["controller"]?.ToString() ?? string.Empty;
        string action = ViewContext.RouteData.Values["action"]?.ToString() ?? string.Empty;

        bool isAdminOrderDetail =
            string.Equals(area, "Admin", StringComparison.OrdinalIgnoreCase)
            && string.Equals(controller, "Order", StringComparison.OrdinalIgnoreCase)
            && string.Equals(action, "Details", StringComparison.OrdinalIgnoreCase);

        bool isCustomerOrderDetail =
            string.IsNullOrWhiteSpace(area)
            && string.Equals(controller, "Customer", StringComparison.OrdinalIgnoreCase)
            && string.Equals(action, "OrderDetail", StringComparison.OrdinalIgnoreCase);

        if (!isAdminOrderDetail && !isCustomerOrderDetail)
        {
            return;
        }

        int orderId = ResolveOrderId();
        if (orderId <= 0)
        {
            return;
        }

        string audience = isAdminOrderDetail ? "admin" : "customer";
        string markup = $"""
            <div id="shippingTimelineHost"
                 data-shipping-timeline-host="true"
                 data-order-id="{orderId}"
                 data-audience="{audience}"></div>
            <script src="/js/shared/shipping-timeline.js?v=1.0.0"></script>
            """;

        output.PostContent.AppendHtml(markup);
    }

    private int ResolveOrderId()
    {
        if (ViewContext.ViewData.Model is Orders order && order.OrderId > 0)
        {
            return order.OrderId;
        }

        object? routeId = ViewContext.RouteData.Values["id"];
        if (routeId != null && int.TryParse(routeId.ToString(), out int parsedRouteId))
        {
            return parsedRouteId;
        }

        string queryId = ViewContext.HttpContext.Request.Query["id"].ToString();
        return int.TryParse(queryId, out int parsedQueryId) ? parsedQueryId : 0;
    }
}
