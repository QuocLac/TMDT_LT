using Microsoft.EntityFrameworkCore;
using System;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class OrderStateService : IOrderStateService
{
    private readonly ApplicationDbContext _context;

    public OrderStateService(ApplicationDbContext context)
    {
        _context = context;
    }

    public OrderTransitionResult Transition(
        Orders order,
        string targetStatus,
        string note,
        DateTime? occurredAt = null)
    {
        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Chuyển trạng thái đơn hàng phải chạy bên trong database transaction.");
        }

        var previousStatus = order.Status?.Trim() ?? string.Empty;
        targetStatus = targetStatus?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(targetStatus))
        {
            throw new InvalidOperationException("Trạng thái đích không hợp lệ.");
        }

        if (string.Equals(previousStatus, targetStatus, StringComparison.Ordinal))
        {
            return new OrderTransitionResult(
                Applied: false,
                AlreadyApplied: true,
                PreviousStatus: previousStatus,
                CurrentStatus: targetStatus);
        }

        if (!OrderStatuses.CanTransition(previousStatus, targetStatus))
        {
            throw new InvalidOperationException(
                $"Không thể chuyển trạng thái đơn từ '{previousStatus}' sang '{targetStatus}'.");
        }

        var timestamp = occurredAt ?? DateTime.Now;
        order.Status = targetStatus;

        if (string.Equals(targetStatus, OrderStatuses.Completed, StringComparison.Ordinal)
            && !order.CompletedDate.HasValue)
        {
            order.CompletedDate = timestamp;
        }

        _context.OrderHistories.Add(new OrderHistory
        {
            OrderId = order.OrderId,
            Status = targetStatus,
            UpdatedAt = timestamp,
            Note = string.IsNullOrWhiteSpace(note)
                ? $"Chuyển trạng thái từ '{previousStatus}' sang '{targetStatus}'."
                : note.Trim()
        });

        return new OrderTransitionResult(
            Applied: true,
            AlreadyApplied: false,
            PreviousStatus: previousStatus,
            CurrentStatus: targetStatus);
    }
}
