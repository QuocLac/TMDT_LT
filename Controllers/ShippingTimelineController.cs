using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Controllers;

[ApiController]
[Authorize]
[Route("api/shipping/timeline")]
public sealed class ShippingTimelineController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ShippingTimelineController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetTimeline(
        int orderId,
        CancellationToken cancellationToken)
    {
        bool isAdmin = IsAdminUser();
        int customerId = GetCustomerId();

        if (!isAdmin && customerId <= 0)
        {
            return Forbid();
        }

        var order = await _context.Orders
            .AsNoTracking()
            .Include(current => current.Shipping)
                .ThenInclude(shipping => shipping.ShippingEvents)
            .FirstOrDefaultAsync(
                current => current.OrderId == orderId
                    && (isAdmin || current.CustomerId == customerId),
                cancellationToken);

        if (order == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy đơn hàng hoặc bạn không có quyền xem vận đơn này."
            });
        }

        var shipping = order.Shipping
            .OrderByDescending(current => current.ShippingId)
            .FirstOrDefault();

        if (shipping == null)
        {
            return Ok(new
            {
                success = true,
                exists = false,
                isAdmin,
                orderId,
                orderStatus = order.Status,
                currentStatus = ShippingStatuses.Pending,
                progress = 5,
                message = "Đơn hàng chưa được phát hành vận đơn.",
                events = Array.Empty<object>()
            });
        }

        List<object> events = shipping.ShippingEvents
            .OrderByDescending(current => current.ReceivedAt)
            .ThenByDescending(current => current.ShippingEventId)
            .Select(current => (object)new
            {
                id = current.ShippingEventId,
                title = GetFriendlyStatus(current.MappedStatus, current.ProviderStatus),
                detail = GetFriendlyDetail(current.ProviderStatus, current.MappedStatus),
                occurredAt = current.ReceivedAt,
                mappedStatus = current.MappedStatus,
                providerStatus = isAdmin ? current.ProviderStatus : null,
                eventType = isAdmin ? current.EventType : null,
                processingStatus = isAdmin ? current.Status : null,
                error = isAdmin ? current.ErrorMessage : null
            })
            .ToList();

        if (events.Count == 0)
        {
            events.Add(new
            {
                id = 0L,
                title = shipping.Status ?? ShippingStatuses.Pending,
                detail = "Hệ thống đang chờ cập nhật tiếp theo từ đơn vị vận chuyển.",
                occurredAt = shipping.UpdatedAt ?? shipping.CreatedAt,
                mappedStatus = shipping.Status,
                providerStatus = isAdmin ? shipping.ProviderStatus : null,
                eventType = isAdmin ? "CURRENT_STATE" : null,
                processingStatus = isAdmin ? ShippingEventStatuses.Processed : null,
                error = isAdmin ? shipping.LastError : null
            });
        }

        return Ok(new
        {
            success = true,
            exists = true,
            isAdmin,
            orderId,
            orderStatus = order.Status,
            provider = shipping.Carrier ?? shipping.ProviderCode ?? "Chưa chỉ định",
            providerCode = shipping.ProviderCode,
            trackingNumber = shipping.TrackingNumber,
            currentStatus = shipping.Status ?? ShippingStatuses.Unknown,
            providerStatus = isAdmin ? shipping.ProviderStatus : null,
            progress = GetProgress(shipping.Status),
            estimatedDelivery = shipping.EstimatedDelivery,
            shippedAt = shipping.ShippedDate,
            deliveredAt = shipping.DeliveredDate,
            cancelledAt = shipping.CancelledAt,
            lastWebhookAt = shipping.LastWebhookAt,
            shippingFee = shipping.ShippingFee,
            codAmount = shipping.CodAmount,
            insuranceValue = isAdmin ? shipping.InsuranceValue : null,
            serviceTypeId = isAdmin ? shipping.ServiceTypeId : null,
            note = isAdmin ? shipping.Note : null,
            lastError = isAdmin ? shipping.LastError : null,
            events
        });
    }

    private bool IsAdminUser()
    {
        if (User.IsInRole("Admin"))
        {
            return true;
        }

        return User.Claims.Any(claim =>
            string.Equals(claim.Value, "Admin", StringComparison.OrdinalIgnoreCase)
            && (claim.Type == ClaimTypes.Role
                || claim.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase)
                || string.Equals(claim.Type, "Role", StringComparison.OrdinalIgnoreCase)));
    }

    private int GetCustomerId()
    {
        string? raw = User.FindFirstValue("CustomerId");
        return int.TryParse(raw, out int customerId) ? customerId : 0;
    }

    private static int GetProgress(string? status) => status switch
    {
        ShippingStatuses.Pending => 5,
        ShippingStatuses.AwaitingManual => 10,
        ShippingStatuses.Created => 20,
        ShippingStatuses.Picking => 35,
        ShippingStatuses.InTransit => 70,
        ShippingStatuses.Delivered => 100,
        ShippingStatuses.DeliveryFailed => 75,
        ShippingStatuses.Returning => 60,
        ShippingStatuses.Returned => 100,
        ShippingStatuses.Cancelled => 100,
        ShippingStatuses.Exception => 50,
        _ => 10
    };

    private static string GetFriendlyStatus(
        string? mappedStatus,
        string? providerStatus)
    {
        if (!string.IsNullOrWhiteSpace(mappedStatus))
        {
            return mappedStatus;
        }

        return NormalizeProviderStatus(providerStatus) switch
        {
            "ready_to_pick" => ShippingStatuses.Created,
            "picking" or "money_collect_picking" => ShippingStatuses.Picking,
            "picked" or "storing" or "transporting" or "sorting"
                or "delivering" or "money_collect_delivering" => ShippingStatuses.InTransit,
            "delivered" => ShippingStatuses.Delivered,
            "delivery_fail" or "waiting_to_return" => ShippingStatuses.DeliveryFailed,
            "return" or "return_transporting" or "return_sorting" or "returning" => ShippingStatuses.Returning,
            "returned" => ShippingStatuses.Returned,
            "cancel" or "cancelled" => ShippingStatuses.Cancelled,
            "exception" or "damage" or "lost" or "warehouse_damage" => ShippingStatuses.Exception,
            _ => ShippingStatuses.Unknown
        };
    }

    private static string GetFriendlyDetail(
        string? providerStatus,
        string? mappedStatus)
    {
        return NormalizeProviderStatus(providerStatus) switch
        {
            "ready_to_pick" => "Vận đơn đã được tạo và đang chờ nhân viên vận chuyển đến lấy hàng.",
            "picking" => "Nhân viên vận chuyển đang đến kho để nhận kiện hàng.",
            "money_collect_picking" => "Đơn vị vận chuyển đang nhận hàng và đối soát phí tại kho.",
            "picked" => "Kiện hàng đã được lấy khỏi kho của cửa hàng.",
            "storing" => "Kiện hàng đang được lưu tại kho trung chuyển.",
            "transporting" => "Kiện hàng đang di chuyển giữa các kho vận chuyển.",
            "sorting" => "Kiện hàng đang được phân loại để chuyển đến khu vực giao nhận.",
            "delivering" => "Nhân viên giao hàng đang mang kiện hàng đến địa chỉ nhận.",
            "money_collect_delivering" => "Đơn đang được giao và sẽ thu tiền COD khi giao thành công.",
            "delivered" => "Đơn vị vận chuyển xác nhận kiện hàng đã được giao thành công.",
            "delivery_fail" => "Lần giao hàng gần nhất chưa thành công. Đơn vị vận chuyển sẽ cập nhật bước tiếp theo.",
            "waiting_to_return" => "Kiện hàng đang chờ thực hiện quy trình hoàn về cửa hàng.",
            "return" or "return_transporting" or "return_sorting" or "returning" =>
                "Kiện hàng đang được vận chuyển hoàn về cửa hàng.",
            "returned" => "Kiện hàng đã được hoàn về cửa hàng.",
            "cancel" or "cancelled" => "Vận đơn đã được hủy.",
            "exception" => "Đơn vị vận chuyển báo phát sinh sự cố cần kiểm tra.",
            "damage" or "warehouse_damage" => "Đơn vị vận chuyển báo kiện hàng có dấu hiệu hư hỏng.",
            "lost" => "Đơn vị vận chuyển báo kiện hàng đang được xử lý theo quy trình thất lạc.",
            _ => !string.IsNullOrWhiteSpace(mappedStatus)
                ? $"Trạng thái vận chuyển được cập nhật thành: {mappedStatus}."
                : "Hệ thống đã nhận một cập nhật mới từ đơn vị vận chuyển."
        };
    }

    private static string NormalizeProviderStatus(string? status) =>
        status?.Trim().ToLowerInvariant() ?? string.Empty;
}
