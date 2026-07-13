using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;
using System.Linq;
using System.Net;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.TagHelpers;

[HtmlTargetElement("body")]
public sealed class CustomerVnPayRetryBodyTagHelper
    : TagHelper
{
    private readonly PaymentExpirationPolicy
        _expirationPolicy;
    private readonly IAntiforgery _antiforgery;

    public CustomerVnPayRetryBodyTagHelper(
        PaymentExpirationPolicy expirationPolicy,
        IAntiforgery antiforgery)
    {
        _expirationPolicy = expirationPolicy;
        _antiforgery = antiforgery;
    }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; }
        = null!;

    public override int Order => 420;

    public override void Process(
        TagHelperContext context,
        TagHelperOutput output)
    {
        if (!IsCustomerOrderDetail())
        {
            return;
        }

        if (ViewContext.ViewData.Model
            is not Orders order
            || order.OrderId <= 0)
        {
            return;
        }

        Payments? payment = order.Payments
            ?.OrderByDescending(current =>
                current.PaymentId)
            .FirstOrDefault();

        if (payment == null
            || !string.Equals(
                payment.PaymentMethod,
                PaymentMethods.VnPay,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                payment.PaymentStatus,
                PaymentStatuses.AwaitingGateway,
                StringComparison.Ordinal)
            || !string.Equals(
                order.Status,
                OrderStatuses.Pending,
                StringComparison.Ordinal)
            || !order.IsStockDeducted)
        {
            return;
        }

        DateTime? deadline =
            _expirationPolicy.GetDeadline(payment);

        if (deadline.HasValue
            && DateTime.Now >= deadline.Value)
        {
            return;
        }

        var tokenSet = _antiforgery
            .GetAndStoreTokens(
                ViewContext.HttpContext);

        string requestToken =
            WebUtility.HtmlEncode(
                tokenSet.RequestToken
                    ?? string.Empty);
        string fieldName =
            WebUtility.HtmlEncode(
                tokenSet.FormFieldName);
        string pathBase =
            ViewContext.HttpContext
                .Request.PathBase.Value
            ?? string.Empty;
        string retryUrl =
            WebUtility.HtmlEncode(
                $"{pathBase}/Payment/RetryVnPay");
        string statusUrl =
            WebUtility.HtmlEncode(
                $"{pathBase}/Payment/Status"
                + $"?orderId={order.OrderId}");
        string expiresAt =
            deadline.HasValue
                ? deadline.Value.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fff")
                : string.Empty;
        string expiresText =
            deadline.HasValue
                ? deadline.Value.ToString(
                    "dd/MM/yyyy HH:mm")
                : "theo thời hạn giữ hàng của hệ thống";

        output.PostContent.AppendHtml(
            $"""
            <link rel="stylesheet"
                  href="/css/shared/customer-vnpay-retry.css?v=1.0.0" />
            <section id="customerVnPayRetryCard"
                     class="customer-vnpay-retry-card"
                     data-vnpay-retry-card="true"
                     data-order-id="{order.OrderId}"
                     data-status-url="{statusUrl}"
                     data-expires-at="{expiresAt}"
                     data-awaiting-status="{WebUtility.HtmlEncode(PaymentStatuses.AwaitingGateway)}"
                     data-pending-order-status="{WebUtility.HtmlEncode(OrderStatuses.Pending)}"
                     hidden
                     aria-live="polite">
                <div class="customer-vnpay-retry-icon"
                     aria-hidden="true">
                    <i class="fas fa-credit-card"></i>
                </div>

                <div class="customer-vnpay-retry-content">
                    <strong>Thanh toán VNPAY chưa hoàn tất</strong>
                    <p>
                        Giao dịch trước đã bị đóng hoặc hủy giữa chừng.
                        Đơn hàng vẫn đang được giữ để bạn mở lại cổng thanh toán.
                    </p>

                    <div class="customer-vnpay-retry-deadline">
                        Hạn thanh toán:
                        <strong>{WebUtility.HtmlEncode(expiresText)}</strong>
                        <span data-vnpay-countdown></span>
                    </div>

                    <form action="{retryUrl}"
                          method="post"
                          data-vnpay-retry-form>
                        <input type="hidden"
                               name="{fieldName}"
                               value="{requestToken}" />
                        <input type="hidden"
                               name="orderId"
                               value="{order.OrderId}" />

                        <button type="submit"
                                class="customer-vnpay-retry-button"
                                data-vnpay-retry-button>
                            <i class="fas fa-rotate-right"></i>
                            Thanh toán lại VNPAY
                        </button>
                    </form>

                    <div class="customer-vnpay-retry-message"
                         data-vnpay-retry-message
                         hidden></div>
                </div>
            </section>
            <script src="/js/shared/customer-vnpay-retry.js?v=1.0.0"
                    defer></script>
            """);
    }

    private bool IsCustomerOrderDetail()
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

        return string.IsNullOrWhiteSpace(area)
            && controller.Equals(
                "Customer",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "OrderDetail",
                StringComparison.OrdinalIgnoreCase);
    }
}
