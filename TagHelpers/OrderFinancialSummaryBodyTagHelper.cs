using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Commerce;

namespace TMDT_LT.TagHelpers;

[HtmlTargetElement("body")]
public sealed class OrderFinancialSummaryBodyTagHelper
    : TagHelper
{
    private static readonly CultureInfo VietnameseCulture =
        CultureInfo.GetCultureInfo("vi-VN");

    private readonly ApplicationDbContext _context;

    public OrderFinancialSummaryBodyTagHelper(
        ApplicationDbContext context)
    {
        _context = context;
    }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override int Order => 320;

    public override async Task ProcessAsync(
        TagHelperContext context,
        TagHelperOutput output)
    {
        RouteMode routeMode = ResolveRouteMode();

        if (routeMode == RouteMode.None)
        {
            return;
        }

        int orderId = ResolveOrderId();
        if (orderId <= 0)
        {
            return;
        }

        if (!HasAccess(routeMode))
        {
            return;
        }

        Orders? order = await _context.Orders
            .AsNoTracking()
            .Include(current =>
                current.OrderDetails)
            .Include(current =>
                current.Payments)
            .Include(current =>
                current.Shipping)
            .FirstOrDefaultAsync(
                current =>
                    current.OrderId == orderId,
                ViewContext.HttpContext
                    .RequestAborted);

        if (order == null)
        {
            return;
        }

        if (routeMode == RouteMode.Customer
            && !CustomerOwnsOrder(order))
        {
            return;
        }

        var summary =
            OrderFinancialSummaryViewModel
                .Create(order);

        bool isAdmin =
            routeMode == RouteMode.Admin;

        output.PostContent.AppendHtml(
            BuildMarkup(summary, isAdmin));
    }

    private RouteMode ResolveRouteMode()
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

        bool customerRoute =
            string.IsNullOrWhiteSpace(area)
            && controller.Equals(
                "Customer",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "OrderDetail",
                StringComparison.OrdinalIgnoreCase);

        if (customerRoute)
        {
            return RouteMode.Customer;
        }

        bool adminRoute =
            area.Equals(
                "Admin",
                StringComparison.OrdinalIgnoreCase)
            && controller.Equals(
                "Order",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "Details",
                StringComparison.OrdinalIgnoreCase);

        return adminRoute
            ? RouteMode.Admin
            : RouteMode.None;
    }

    private bool HasAccess(RouteMode routeMode)
    {
        var user = ViewContext.HttpContext.User;

        if (routeMode == RouteMode.Admin)
        {
            return user.IsInRole("Admin");
        }

        return user.Identity?.IsAuthenticated == true
            && GetCurrentCustomerId() > 0;
    }

    private bool CustomerOwnsOrder(Orders order) =>
        order.CustomerId
            == GetCurrentCustomerId();

    private int GetCurrentCustomerId()
    {
        string? raw = ViewContext.HttpContext.User
            .FindFirstValue("CustomerId");

        return int.TryParse(raw, out int customerId)
            ? customerId
            : 0;
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

        string queryId =
            ViewContext.HttpContext.Request
                .Query["id"]
                .ToString();

        if (int.TryParse(
                queryId,
                out int parsedQueryId))
        {
            return parsedQueryId;
        }

        string queryOrderId =
            ViewContext.HttpContext.Request
                .Query["orderId"]
                .ToString();

        return int.TryParse(
            queryOrderId,
            out int parsedOrderId)
                ? parsedOrderId
                : 0;
    }

    private static string BuildMarkup(
        OrderFinancialSummaryViewModel summary,
        bool isAdmin)
    {
        string mode = isAdmin
            ? "admin"
            : "customer";

        string legacyClass =
            summary.IsLegacyFallback
                ? " order-financial-summary__source--legacy"
                : string.Empty;

        string voucherMarkup =
            string.IsNullOrWhiteSpace(
                summary.AppliedVoucherCode)
                ? string.Empty
                : $"""
                   <div class="order-financial-summary__row">
                       <span>Mã ưu đãi</span>
                       <strong class="order-financial-summary__voucher">
                           {Encode(summary.AppliedVoucherCode)}
                       </strong>
                   </div>
                   """;

        string adminMarkup = isAdmin
            ? BuildAdminMarkup(summary)
            : string.Empty;

        return $"""
            <link rel="stylesheet"
                  href="/css/shared/order-financial-summary.css?v=1.0.0" />
            <section class="order-financial-summary"
                     data-order-financial-summary="true"
                     data-mode="{mode}"
                     aria-label="Chi tiết thanh toán đơn hàng">
                <header class="order-financial-summary__header">
                    <div>
                        <div class="order-financial-summary__eyebrow">
                            Đơn #{summary.OrderId}
                        </div>
                        <h3 class="order-financial-summary__title">
                            Chi tiết thanh toán
                        </h3>
                    </div>
                    <span class="order-financial-summary__source{legacyClass}">
                        {Encode(summary.SnapshotSource)}
                    </span>
                </header>

                <div class="order-financial-summary__body">
                    <div class="order-financial-summary__row">
                        <span>Tiền hàng</span>
                        <strong>{Money(summary.SubtotalAmount)}</strong>
                    </div>

                    <div class="order-financial-summary__row order-financial-summary__row--discount">
                        <span>Giảm giá</span>
                        <strong>-{Money(summary.DiscountAmount)}</strong>
                    </div>

                    {voucherMarkup}

                    <div class="order-financial-summary__row">
                        <span>Phí vận chuyển</span>
                        <strong>{Money(summary.ShippingFee)}</strong>
                    </div>

                    <div class="order-financial-summary__row">
                        <span>Thuế</span>
                        <strong>{Money(summary.TaxAmount)}</strong>
                    </div>

                    <div class="order-financial-summary__total">
                        <span>Tổng thanh toán</span>
                        <strong>{Money(summary.GrandTotalAmount)}</strong>
                    </div>

                    {adminMarkup}
                </div>
            </section>
            <script src="/js/shared/order-financial-summary.js?v=1.0.0"
                    defer></script>
            """;
    }

    private static string BuildAdminMarkup(
        OrderFinancialSummaryViewModel summary)
    {
        string consistencyClass =
            summary.IsConsistent
                ? "order-financial-summary__check--ok"
                : "order-financial-summary__check--warning";

        string consistencyText =
            summary.IsConsistent
                ? "Khớp snapshot"
                : $"Lệch {Money(
                    Math.Abs(
                        summary.ReconciliationDelta))}";

        string paymentAmount =
            summary.PaymentAmount.HasValue
                ? Money(summary.PaymentAmount.Value)
                : "Chưa ghi nhận";

        string checkoutReference =
            string.IsNullOrWhiteSpace(
                summary.CheckoutReference)
                ? "Không có"
                : Encode(
                    summary.CheckoutReference);

        return $"""
            <details class="order-financial-summary__audit">
                <summary>Thông tin đối soát Admin</summary>

                <div class="order-financial-summary__audit-grid">
                    <div>
                        <span>Kiểm tra công thức</span>
                        <strong class="{consistencyClass}">
                            {consistencyText}
                        </strong>
                    </div>
                    <div>
                        <span>Phương thức</span>
                        <strong>{EncodeOrDash(summary.PaymentMethod)}</strong>
                    </div>
                    <div>
                        <span>Trạng thái thanh toán</span>
                        <strong>{EncodeOrDash(summary.PaymentStatus)}</strong>
                    </div>
                    <div>
                        <span>Số tiền Payment</span>
                        <strong>{paymentAmount}</strong>
                    </div>
                    <div>
                        <span>Checkout reference</span>
                        <strong>{checkoutReference}</strong>
                    </div>
                </div>
            </details>
            """;
    }

    private static string Money(decimal value) =>
        $"{value.ToString(
            "N0",
            VietnameseCulture)} đ";

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    private static string EncodeOrDash(
        string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "—"
            : Encode(value);

    private enum RouteMode
    {
        None,
        Customer,
        Admin
    }
}
