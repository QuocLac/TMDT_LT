using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TMDT_LT.Models;
using TMDT_LT.Services;

namespace TMDT_LT.Filters;

public sealed class CheckoutIdempotencyFilter
    : IAsyncActionFilter,
      IOrderedFilter
{
    private readonly ICheckoutIdempotencyService
        _idempotencyService;

    public CheckoutIdempotencyFilter(
        ICheckoutIdempotencyService
            idempotencyService)
    {
        _idempotencyService = idempotencyService;
    }

    public int Order => -500;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (!IsPlaceOrder(context))
        {
            await next();
            return;
        }

        int customerId = ResolveCustomerId(context);
        IFormCollection form =
            await context.HttpContext.Request
                .ReadFormAsync(
                    context.HttpContext.RequestAborted);

        string rawKey = form[
            CheckoutIdempotencyConstants.FormFieldName
        ].ToString();

        if (!Guid.TryParseExact(
                rawKey,
                "N",
                out Guid parsedKey))
        {
            Reject(
                context,
                "Phiên checkout đã hết hạn. Vui lòng tải lại trang thanh toán.");
            return;
        }

        string idempotencyKey =
            parsedKey.ToString("N");
        string voucherCode = form[
            "AppliedVoucherCode"
        ].ToString().Trim().ToUpperInvariant();

        CheckoutInvoiceRequestSnapshot invoiceSnapshot;
        try
        {
            invoiceSnapshot =
                BuildInvoiceRequestSnapshot(form);
        }
        catch (CheckoutInvoiceValidationException ex)
        {
            Reject(context, ex.Message);
            return;
        }

        context.HttpContext.Items[
            CheckoutIdempotencyConstants.KeyItemName
        ] = idempotencyKey;
        context.HttpContext.Items[
            CheckoutIdempotencyConstants.VoucherItemName
        ] = voucherCode;

        context.HttpContext.Items[
            CheckoutInvoiceRequestConstants.SnapshotItemName
        ] = invoiceSnapshot;

        int selectedAddressId = int.TryParse(
            form["SelectedAddressId"],
            out int addressId)
                ? addressId
                : 0;

        string paymentMethod =
            Normalize(form["PaymentMethod"]);
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            paymentMethod =
                Normalize(form["paymentMethod"]);
        }

        string requestHash = ComputeRequestHash(
            customerId,
            selectedAddressId,
            paymentMethod,
            voucherCode,
            NormalizeSelectedItems(
                form["selectedItems"]),
            Normalize(form["buyNowVariantId"]),
            Normalize(form["buyNowQty"]),
            invoiceSnapshot.CanonicalFingerprint);

        CheckoutClaimResult claim =
            await _idempotencyService.BeginAsync(
                new CheckoutClaimCommand(
                    customerId,
                    idempotencyKey,
                    requestHash,
                    selectedAddressId,
                    paymentMethod),
                context.HttpContext.RequestAborted);

        if (claim.State
            == CheckoutClaimState.Completed
            && claim.OrderId.HasValue)
        {
            context.Result =
                new RedirectToActionResult(
                    "OrderPlaced",
                    "Checkout",
                    new RouteValueDictionary
                    {
                        ["orderId"] = claim.OrderId.Value
                    });
            return;
        }

        if (claim.State
            == CheckoutClaimState.InProgress)
        {
            SetTempData(
                context,
                "Yêu cầu đặt hàng đang được xử lý. Vui lòng kiểm tra danh sách đơn hàng sau ít phút.");

            context.Result =
                new RedirectToActionResult(
                    "Orders",
                    "Customer",
                    null);
            return;
        }

        if (claim.State
            == CheckoutClaimState.Conflict)
        {
            Reject(context, claim.Message);
            return;
        }

        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch (Exception ex)
        {
            await _idempotencyService.MarkFailedAsync(
                idempotencyKey,
                customerId,
                ex.Message,
                context.HttpContext.RequestAborted);
            throw;
        }

        int? orderId = ResolveCreatedOrderId(
            context,
            executed);

        if (executed.Exception == null
            && orderId.HasValue
            && orderId.Value > 0)
        {
            await _idempotencyService.MarkCompletedAsync(
                idempotencyKey,
                customerId,
                orderId.Value,
                context.HttpContext.RequestAborted);
            return;
        }

        await _idempotencyService.MarkFailedAsync(
            idempotencyKey,
            customerId,
            executed.Exception?.Message
                ?? "Checkout không tạo được đơn hàng.",
            context.HttpContext.RequestAborted);
    }

    private static bool IsPlaceOrder(
        ActionExecutingContext context)
    {
        string controller =
            context.RouteData.Values["controller"]
                ?.ToString()
            ?? string.Empty;
        string action =
            context.RouteData.Values["action"]
                ?.ToString()
            ?? string.Empty;

        return context.HttpContext.Request.Method
                .Equals(
                    "POST",
                    StringComparison.OrdinalIgnoreCase)
            && controller.Equals(
                "Checkout",
                StringComparison.OrdinalIgnoreCase)
            && action.Equals(
                "PlaceOrder",
                StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveCustomerId(
        ActionExecutingContext context)
    {
        string? raw = context.HttpContext.User
            .FindFirstValue("CustomerId");

        return int.TryParse(raw, out int customerId)
            ? customerId
            : 0;
    }

    private static int? ResolveCreatedOrderId(
        ActionExecutingContext context,
        ActionExecutedContext executed)
    {
        object? item = context.HttpContext.Items[
            CheckoutIdempotencyConstants
                .CreatedOrderIdItemName
        ];

        if (item != null
            && int.TryParse(
                item.ToString(),
                out int itemOrderId))
        {
            return itemOrderId;
        }

        if (executed.Result
            is RedirectToActionResult redirect
            && redirect.RouteValues != null
            && redirect.RouteValues.TryGetValue(
                "orderId",
                out object? routeOrderId)
            && int.TryParse(
                routeOrderId?.ToString(),
                out int parsedOrderId))
        {
            return parsedOrderId;
        }

        return null;
    }

    private static void Reject(
        ActionExecutingContext context,
        string message)
    {
        SetTempData(context, message);

        IFormCollection? form =
            context.HttpContext.Request
                .HasFormContentType
                ? context.HttpContext.Request.Form
                : null;

        context.Result =
            new RedirectToActionResult(
                "Index",
                "Checkout",
                new RouteValueDictionary
                {
                    ["selectedItems"] =
                        form?["selectedItems"]
                            .ToString(),
                    ["buyNowVariantId"] =
                        form?["buyNowVariantId"]
                            .ToString(),
                    ["buyNowQty"] =
                        form?["buyNowQty"]
                            .ToString()
                });
    }

    private static void SetTempData(
        ActionExecutingContext context,
        string message)
    {
        if (context.Controller is Controller controller)
        {
            controller.TempData["Error"] = message;
        }
    }

    private static string ComputeRequestHash(
        int customerId,
        int selectedAddressId,
        string paymentMethod,
        string voucherCode,
        string selectedItems,
        string buyNowVariantId,
        string buyNowQty,
        string invoiceFingerprint)
    {
        string canonical = string.Join(
            "|",
            customerId,
            selectedAddressId,
            paymentMethod,
            voucherCode,
            selectedItems,
            buyNowVariantId,
            buyNowQty,
            invoiceFingerprint);

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical));

        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }

    private static string NormalizeSelectedItems(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return string.Join(
            ",",
            raw.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries
                        | StringSplitOptions.TrimEntries)
                .Select(value =>
                    int.TryParse(value, out int parsed)
                        ? parsed
                        : 0)
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value));
    }

    private static CheckoutInvoiceRequestSnapshot
        BuildInvoiceRequestSnapshot(
            IFormCollection form)
    {
        bool isRequested = IsTruthy(
            form["RequestVatInvoice"]);

        if (!isRequested)
        {
            return CheckoutInvoiceRequestSnapshot.None;
        }

        string buyerType = NormalizeBuyerType(
            form["InvoiceBuyerType"]);
        string buyerName = RequiredText(
            form["InvoiceBuyerName"],
            "Tên người mua hoặc đơn vị",
            200);
        string buyerAddress = RequiredText(
            form["InvoiceAddress"],
            "Địa chỉ xuất hóa đơn",
            500);
        string buyerEmail = RequiredText(
            form["InvoiceEmail"],
            "Email nhận hóa đơn",
            200)
            .ToLowerInvariant();
        string? buyerPhone = OptionalText(
            form["InvoicePhone"],
            30);
        string? taxCode = OptionalText(
            form["InvoiceTaxCode"],
            20);

        if (!IsValidEmail(buyerEmail))
        {
            throw new CheckoutInvoiceValidationException(
                "Email nhận hóa đơn không hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(buyerPhone)
            && !Regex.IsMatch(
                buyerPhone,
                @"^[0-9+\s().-]{8,30}$"))
        {
            throw new CheckoutInvoiceValidationException(
                "Số điện thoại nhận hóa đơn không hợp lệ.");
        }

        if (buyerType
                == InvoiceBuyerTypes.Organization
            && string.IsNullOrWhiteSpace(taxCode))
        {
            throw new CheckoutInvoiceValidationException(
                "Vui lòng nhập mã số thuế của tổ chức.");
        }

        if (!string.IsNullOrWhiteSpace(taxCode))
        {
            taxCode = taxCode
                .Replace(" ", string.Empty)
                .ToUpperInvariant();

            if (!Regex.IsMatch(
                    taxCode,
                    @"^[0-9-]{8,20}$"))
            {
                throw new CheckoutInvoiceValidationException(
                    "Mã số thuế chỉ được chứa chữ số và dấu gạch ngang.");
            }
        }

        return new CheckoutInvoiceRequestSnapshot(
            true,
            buyerType,
            buyerName,
            taxCode,
            buyerAddress,
            buyerEmail,
            buyerPhone);
    }

    private static string NormalizeBuyerType(
        string? raw)
    {
        string normalized = Normalize(raw);

        return normalized == "ORGANIZATION"
            ? InvoiceBuyerTypes.Organization
            : InvoiceBuyerTypes.Individual;
    }

    private static bool IsTruthy(string? raw)
    {
        string normalized = Normalize(raw);

        return normalized is "TRUE"
            or "1"
            or "ON"
            or "YES";
    }

    private static string RequiredText(
        string? raw,
        string fieldName,
        int maxLength)
    {
        string value = raw?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CheckoutInvoiceValidationException(
                $"{fieldName} là bắt buộc.");
        }

        if (value.Length > maxLength)
        {
            throw new CheckoutInvoiceValidationException(
                $"{fieldName} vượt quá {maxLength} ký tự.");
        }

        return value;
    }

    private static string? OptionalText(
        string? raw,
        int maxLength)
    {
        string value = raw?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            throw new CheckoutInvoiceValidationException(
                $"Thông tin hóa đơn vượt quá {maxLength} ký tự.");
        }

        return value;
    }

    private static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(
                address.Address,
                value,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Normalize(
        string? value) =>
        value?.Trim().ToUpperInvariant()
        ?? string.Empty;
}
