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
using TMDT_LT.Models.ViewModels.Storefront;

namespace TMDT_LT.Controllers;

[ApiController]
[Authorize]
[Route("api/customer/refunds/timeline")]
public sealed class CustomerRefundTimelineController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public CustomerRefundTimelineController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> GetTimeline(
        int orderId,
        CancellationToken cancellationToken)
    {
        int customerId = GetCurrentCustomerId();
        if (customerId <= 0)
        {
            return Forbid();
        }

        Orders? order = await _context.Orders
            .AsNoTracking()
            .Include(current => current.Payments)
            .Include(current => current.PaymentTransactions)
            .Include(current => current.OrderReturns)
            .FirstOrDefaultAsync(
                current => current.OrderId == orderId
                    && current.CustomerId == customerId,
                cancellationToken);

        if (order == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Không tìm thấy đơn hàng hoặc bạn không có quyền xem thông tin hoàn tiền."
            });
        }

        Payments? payment = order.Payments
            .OrderByDescending(current => current.PaymentId)
            .FirstOrDefault();

        OrderReturns? latestReturn = order.OrderReturns
            .OrderByDescending(current => current.CreatedAt)
            .ThenByDescending(current => current.ReturnId)
            .FirstOrDefault();

        List<PaymentTransactions> refundEvents = order.PaymentTransactions
            .Where(current => current.EventType == PaymentEventTypes.Refund)
            .OrderBy(current => current.ReceivedAt)
            .ThenBy(current => current.PaymentTransactionId)
            .ToList();

        PaymentTransactions? latestRefund = refundEvents.LastOrDefault();

        bool paymentRefunded = payment?.PaymentStatus == PaymentStatuses.Refunded;
        bool paymentAwaitingRefund =
            payment?.PaymentStatus == PaymentStatuses.AwaitingRefund;
        bool returnRefunded = latestReturn?.Status == ReturnStatuses.Refunded;
        bool refundProcessed =
            latestRefund?.Status == PaymentEventStatuses.Processed;

        bool exists = refundEvents.Count > 0
            || paymentRefunded
            || paymentAwaitingRefund
            || returnRefunded;

        if (!exists)
        {
            return Ok(new CustomerRefundTimelineViewModel
            {
                Exists = false,
                OrderId = order.OrderId
            });
        }

        string statusCode = ResolveStatusCode(
            latestRefund,
            paymentRefunded,
            returnRefunded,
            paymentAwaitingRefund);
        bool completed = statusCode == "completed";

        string refundType = ResolveRefundType(
            latestRefund,
            order.Status,
            latestReturn);
        string method = FriendlyPaymentMethod(payment?.PaymentMethod);
        string destination = ResolveDestination(payment?.PaymentMethod);
        decimal amount = latestRefund?.Amount
            ?? payment?.Amount
            ?? order.TotalAmount
            ?? 0;

        DateTime? updatedAt = latestRefund?.ProcessedAt
            ?? latestRefund?.ReceivedAt
            ?? latestReturn?.ResolvedAt
            ?? latestReturn?.CreatedAt
            ?? payment?.LastProcessedAt;

        string? transactionReference = completed
            ? latestRefund?.ProviderTransactionId
                ?? payment?.ProviderTransactionId
            : null;

        var events = BuildFriendlyEvents(
            latestRefund,
            latestReturn,
            payment,
            completed);

        return Ok(new CustomerRefundTimelineViewModel
        {
            Exists = true,
            OrderId = order.OrderId,
            RefundType = refundType,
            StatusCode = statusCode,
            StatusText = FriendlyStatusText(statusCode),
            Message = FriendlyMessage(statusCode),
            Amount = amount,
            PaymentMethod = method,
            Destination = destination,
            TransactionReference = transactionReference,
            UpdatedAt = updatedAt,
            Completed = completed,
            PollingRecommended = !completed,
            Events = events
        });
    }

    private int GetCurrentCustomerId()
    {
        string? raw = User.FindFirstValue("CustomerId");
        return int.TryParse(raw, out int customerId)
            ? customerId
            : 0;
    }

    private static string ResolveStatusCode(
        PaymentTransactions? latestRefund,
        bool paymentRefunded,
        bool returnRefunded,
        bool paymentAwaitingRefund)
    {
        if (latestRefund?.Status == PaymentEventStatuses.Processed
            || paymentRefunded
            || returnRefunded)
        {
            return "completed";
        }

        if (latestRefund?.Status == PaymentEventStatuses.Failed
            || latestRefund?.Status == PaymentEventStatuses.Rejected)
        {
            return "attention";
        }

        if (latestRefund?.Status == PaymentEventStatuses.RequiresReview)
        {
            return "review";
        }

        if (latestRefund?.Status == PaymentEventStatuses.Processing
            || latestRefund?.Status == PaymentEventStatuses.Received)
        {
            return "processing";
        }

        return paymentAwaitingRefund
            ? "review"
            : "processing";
    }

    private static string ResolveRefundType(
        PaymentTransactions? latestRefund,
        string? orderStatus,
        OrderReturns? latestReturn)
    {
        string key = latestRefund?.IdempotencyKey ?? string.Empty;

        if (key.StartsWith(
            "REFUND:CANCEL:",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Hoàn tiền do hủy đơn";
        }

        if (key.StartsWith(
            "REFUND:RETURN:",
            StringComparison.OrdinalIgnoreCase)
            || latestReturn != null)
        {
            return "Hoàn tiền trả hàng";
        }

        return orderStatus == OrderStatuses.Cancelled
            ? "Hoàn tiền do hủy đơn"
            : "Hoàn tiền đơn hàng";
    }

    private static string FriendlyPaymentMethod(string? method)
    {
        if (string.Equals(
            method,
            PaymentMethods.VnPay,
            StringComparison.OrdinalIgnoreCase))
        {
            return "VNPAY";
        }

        if (string.Equals(
            method,
            PaymentMethods.BankTransfer,
            StringComparison.OrdinalIgnoreCase))
        {
            return "Chuyển khoản ngân hàng";
        }

        if (string.Equals(
            method,
            PaymentMethods.Cod,
            StringComparison.OrdinalIgnoreCase))
        {
            return "Thanh toán khi nhận hàng";
        }

        return string.IsNullOrWhiteSpace(method)
            ? "Chưa xác định"
            : method.Trim();
    }

    private static string ResolveDestination(string? method)
    {
        if (string.Equals(
            method,
            PaymentMethods.VnPay,
            StringComparison.OrdinalIgnoreCase))
        {
            return "Phương thức đã dùng khi thanh toán VNPAY";
        }

        return "Tài khoản hoàn tiền đã xác nhận với cửa hàng";
    }

    private static string FriendlyStatusText(string statusCode) =>
        statusCode switch
        {
            "completed" => "Đã hoàn tiền",
            "review" => "Đang đối soát",
            "attention" => "Đang kiểm tra lại",
            _ => "Đang xử lý"
        };

    private static string FriendlyMessage(string statusCode) =>
        statusCode switch
        {
            "completed" =>
                "Khoản hoàn đã được xác nhận. Thời gian tiền về thực tế có thể phụ thuộc ngân hàng hoặc phương thức thanh toán.",
            "review" =>
                "Bộ phận kế toán đang đối soát giao dịch. Hệ thống sẽ tự cập nhật khi khoản hoàn được xác nhận.",
            "attention" =>
                "Giao dịch cần được kiểm tra lại. Bộ phận hỗ trợ đang tiếp tục xử lý và bạn không cần gửi lại yêu cầu.",
            _ =>
                "Yêu cầu hoàn tiền đã được ghi nhận và đang được xử lý."
        };

    private static IReadOnlyList<CustomerRefundTimelineEventViewModel>
        BuildFriendlyEvents(
            PaymentTransactions? latestRefund,
            OrderReturns? latestReturn,
            Payments? payment,
            bool completed)
    {
        var events = new List<CustomerRefundTimelineEventViewModel>();

        DateTime? initiatedAt = latestRefund?.ReceivedAt
            ?? latestReturn?.CreatedAt
            ?? payment?.LastProcessedAt;

        if (initiatedAt.HasValue)
        {
            events.Add(new CustomerRefundTimelineEventViewModel
            {
                Title = "Yêu cầu hoàn tiền được ghi nhận",
                Description =
                    "Hệ thống đã tiếp nhận khoản tiền cần hoàn cho đơn hàng.",
                StatusCode = "completed",
                OccurredAt = initiatedAt.Value
            });
        }

        if (latestRefund != null)
        {
            string statusCode = latestRefund.Status switch
            {
                PaymentEventStatuses.Processed => "completed",
                PaymentEventStatuses.RequiresReview => "review",
                PaymentEventStatuses.Failed => "attention",
                PaymentEventStatuses.Rejected => "attention",
                _ => "processing"
            };

            string title = statusCode switch
            {
                "completed" => "Giao dịch hoàn tiền đã được xác nhận",
                "review" => "Đang đối soát giao dịch",
                "attention" => "Đang kiểm tra lại giao dịch",
                _ => "Đang xử lý với phương thức thanh toán"
            };

            string description = statusCode switch
            {
                "completed" =>
                    "Cửa hàng đã xác nhận hoàn tiền thành công.",
                "review" =>
                    "Bộ phận kế toán đang kiểm tra và đối chiếu giao dịch.",
                "attention" =>
                    "Yêu cầu đang được kiểm tra lại trước khi tiếp tục.",
                _ =>
                    "Yêu cầu đang được gửi và xử lý qua phương thức thanh toán."
            };

            DateTime occurredAt = latestRefund.ProcessedAt
                ?? latestRefund.ReceivedAt;

            bool duplicatesInitial = events.Count > 0
                && events[0].OccurredAt == occurredAt
                && !completed;

            if (!duplicatesInitial)
            {
                events.Add(new CustomerRefundTimelineEventViewModel
                {
                    Title = title,
                    Description = description,
                    StatusCode = statusCode,
                    OccurredAt = occurredAt
                });
            }
        }
        else if (payment?.PaymentStatus == PaymentStatuses.AwaitingRefund)
        {
            events.Add(new CustomerRefundTimelineEventViewModel
            {
                Title = "Đang chuẩn bị hoàn tiền",
                Description =
                    "Cửa hàng đang chuẩn bị và đối soát thông tin giao dịch.",
                StatusCode = "review",
                OccurredAt = payment.LastProcessedAt
                    ?? DateTime.Now
            });
        }

        if (completed
            && events.All(current =>
                current.Title
                    != "Giao dịch hoàn tiền đã được xác nhận"))
        {
            DateTime completedAt = latestRefund?.ProcessedAt
                ?? latestReturn?.ResolvedAt
                ?? payment?.LastProcessedAt
                ?? DateTime.Now;

            events.Add(new CustomerRefundTimelineEventViewModel
            {
                Title = "Giao dịch hoàn tiền đã được xác nhận",
                Description =
                    "Cửa hàng đã xác nhận hoàn tiền thành công.",
                StatusCode = "completed",
                OccurredAt = completedAt
            });
        }

        return events
            .OrderByDescending(current => current.OccurredAt)
            .ToArray();
    }
}
