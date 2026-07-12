using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Commerce;

namespace TMDT_LT.Controllers;

[ApiController]
[Authorize]
[Route("api/customer/invoices/status")]
public sealed class CustomerInvoiceStatusController
    : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public CustomerInvoiceStatusController(
        ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetStatus(
        int orderId,
        CancellationToken cancellationToken)
    {
        int customerId = GetCurrentCustomerId();
        if (customerId <= 0)
        {
            return Forbid();
        }

        OrderInvoiceRequests? request =
            await _context.OrderInvoiceRequests
                .AsNoTracking()
                .Include(current => current.Order)
                .FirstOrDefaultAsync(
                    current =>
                        current.OrderId == orderId
                        && current.Order != null
                        && current.Order.CustomerId
                            == customerId,
                    cancellationToken);

        if (request == null)
        {
            bool orderExists = await _context.Orders
                .AsNoTracking()
                .AnyAsync(
                    current =>
                        current.OrderId == orderId
                        && current.CustomerId
                            == customerId,
                    cancellationToken);

            if (!orderExists)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "Không tìm thấy đơn hàng hoặc bạn không có quyền xem."
                });
            }

            return Ok(new CustomerInvoiceStatusViewModel
            {
                Exists = false,
                OrderId = orderId
            });
        }

        string statusCode =
            ResolveStatusCode(request.Status);
        string? lookupCode =
            NormalizeOptional(
                request.InvoiceLookupCode,
                200);
        string? lookupUrl =
            ResolveHttpsUrl(lookupCode);

        return Ok(new CustomerInvoiceStatusViewModel
        {
            Exists = true,
            OrderId = request.OrderId,
            StatusCode = statusCode,
            StatusText =
                FriendlyStatusText(statusCode),
            Message =
                FriendlyMessage(statusCode),
            BuyerType =
                request.BuyerType
                    == InvoiceBuyerTypes.Organization
                        ? "Tổ chức / doanh nghiệp"
                        : "Cá nhân",
            BuyerName = request.BuyerName,
            TaxCode = request.TaxCode,
            BuyerAddress = request.BuyerAddress,
            BuyerEmail = request.BuyerEmail,
            BuyerPhone = request.BuyerPhone,
            RequestedAt = request.RequestedAt,
            UpdatedAt = request.IssuedAt
                ?? request.ReviewedAt
                ?? request.RequestedAt,
            InvoiceNumber =
                request.Status
                    == InvoiceRequestStatuses.Issued
                    ? NormalizeOptional(
                        request.InvoiceNumber,
                        100)
                    : null,
            InvoiceLookupCode =
                request.Status
                    == InvoiceRequestStatuses.Issued
                    ? lookupCode
                    : null,
            InvoiceLookupUrl =
                request.Status
                    == InvoiceRequestStatuses.Issued
                    ? lookupUrl
                    : null,
            RejectionReason =
                request.Status
                    == InvoiceRequestStatuses.Rejected
                    ? NormalizeOptional(
                        request.AdminNote,
                        500)
                    : null,
            PollingRecommended =
                request.Status
                    == InvoiceRequestStatuses.Pending
        });
    }

    private int GetCurrentCustomerId()
    {
        string? raw =
            User.FindFirstValue("CustomerId");

        return int.TryParse(raw, out int customerId)
            ? customerId
            : 0;
    }

    private static string ResolveStatusCode(
        string? status) =>
        status switch
        {
            InvoiceRequestStatuses.Issued =>
                "issued",
            InvoiceRequestStatuses.Rejected =>
                "rejected",
            InvoiceRequestStatuses.Cancelled =>
                "cancelled",
            _ => "pending"
        };

    private static string FriendlyStatusText(
        string statusCode) =>
        statusCode switch
        {
            "issued" => "Đã phát hành",
            "rejected" => "Chưa thể phát hành",
            "cancelled" => "Đã hủy",
            _ => "Chờ xử lý"
        };

    private static string FriendlyMessage(
        string statusCode) =>
        statusCode switch
        {
            "issued" =>
                "Hóa đơn đã được bộ phận kế toán xác nhận phát hành.",
            "rejected" =>
                "Yêu cầu chưa thể được phát hành với thông tin hiện tại.",
            "cancelled" =>
                "Yêu cầu xuất hóa đơn của đơn hàng đã được hủy.",
            _ =>
                "Thông tin xuất hóa đơn đã được lưu và đang chờ bộ phận kế toán xử lý."
        };

    private static string? ResolveHttpsUrl(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(
                value,
                UriKind.Absolute,
                out Uri? uri)
            && string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }

    private static string? NormalizeOptional(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
