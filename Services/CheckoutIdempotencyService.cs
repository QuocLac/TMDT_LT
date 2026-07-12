using Microsoft.EntityFrameworkCore;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class CheckoutIdempotencyService
    : ICheckoutIdempotencyService
{
    private static readonly TimeSpan ProcessingLease =
        TimeSpan.FromMinutes(5);

    private readonly DbContextOptions<ApplicationDbContext> _options;

    public CheckoutIdempotencyService(
        DbContextOptions<ApplicationDbContext> options)
    {
        _options = options;
    }

    public async Task<CheckoutClaimResult> BeginAsync(
        CheckoutClaimCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.CustomerId <= 0)
        {
            return new CheckoutClaimResult(
                CheckoutClaimState.Conflict,
                null,
                "Không xác định được khách hàng.");
        }

        if (!Guid.TryParseExact(
                command.IdempotencyKey,
                "N",
                out _))
        {
            return new CheckoutClaimResult(
                CheckoutClaimState.Conflict,
                null,
                "Phiên xác nhận đơn hàng không hợp lệ.");
        }

        try
        {
            return await BeginCoreAsync(
                command,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            return await ResolveRaceAsync(
                command,
                cancellationToken);
        }
    }

    public async Task MarkCompletedAsync(
        string idempotencyKey,
        int customerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            new ApplicationDbContext(_options);

        CheckoutAttempts? attempt =
            await context.CheckoutAttempts
                .FirstOrDefaultAsync(
                    current =>
                        current.IdempotencyKey
                            == idempotencyKey
                        && current.CustomerId
                            == customerId,
                    cancellationToken);

        if (attempt == null)
        {
            return;
        }

        bool orderExists = await context.Orders
            .AnyAsync(
                current =>
                    current.OrderId == orderId
                    && current.CustomerId == customerId
                    && current.CheckoutIdempotencyKey
                        == idempotencyKey,
                cancellationToken);

        if (!orderExists)
        {
            await MarkFailedAsync(
                idempotencyKey,
                customerId,
                "Không tìm thấy đơn tương ứng với khóa checkout.",
                cancellationToken);
            return;
        }

        attempt.Status =
            CheckoutAttemptStatuses.Completed;
        attempt.OrderId = orderId;
        attempt.UpdatedAt = DateTime.UtcNow;
        attempt.ExpiresAt = DateTime.UtcNow;
        attempt.FailureReason = null;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        string idempotencyKey,
        int customerId,
        string? error,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            new ApplicationDbContext(_options);

        CheckoutAttempts? attempt =
            await context.CheckoutAttempts
                .FirstOrDefaultAsync(
                    current =>
                        current.IdempotencyKey
                            == idempotencyKey
                        && current.CustomerId
                            == customerId,
                    cancellationToken);

        if (attempt == null
            || attempt.Status
                == CheckoutAttemptStatuses.Completed)
        {
            return;
        }

        int? committedOrderId = await context.Orders
            .Where(current =>
                current.CustomerId == customerId
                && current.CheckoutIdempotencyKey
                    == idempotencyKey)
            .Select(current => (int?)current.OrderId)
            .FirstOrDefaultAsync(cancellationToken);

        if (committedOrderId.HasValue)
        {
            attempt.Status =
                CheckoutAttemptStatuses.Completed;
            attempt.OrderId = committedOrderId;
            attempt.FailureReason = null;
        }
        else
        {
            attempt.Status =
                CheckoutAttemptStatuses.Failed;
            attempt.OrderId = null;
            attempt.FailureReason =
                Truncate(error, 500);
        }

        attempt.UpdatedAt = DateTime.UtcNow;
        attempt.ExpiresAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<CheckoutClaimResult> BeginCoreAsync(
        CheckoutClaimCommand command,
        CancellationToken cancellationToken)
    {
        await using var context =
            new ApplicationDbContext(_options);
        await using var transaction =
            await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        DateTime now = DateTime.UtcNow;

        int? existingOrderId = await context.Orders
            .Where(current =>
                current.CustomerId == command.CustomerId
                && current.CheckoutIdempotencyKey
                    == command.IdempotencyKey)
            .Select(current => (int?)current.OrderId)
            .FirstOrDefaultAsync(cancellationToken);

        CheckoutAttempts? attempt =
            await context.CheckoutAttempts
                .FirstOrDefaultAsync(
                    current =>
                        current.IdempotencyKey
                            == command.IdempotencyKey,
                    cancellationToken);

        if (existingOrderId.HasValue)
        {
            attempt ??= CreateAttempt(
                command,
                now);

            if (context.Entry(attempt).State
                == EntityState.Detached)
            {
                context.CheckoutAttempts.Add(attempt);
            }

            attempt.Status =
                CheckoutAttemptStatuses.Completed;
            attempt.OrderId = existingOrderId;
            attempt.UpdatedAt = now;
            attempt.ExpiresAt = now;
            attempt.FailureReason = null;

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CheckoutClaimResult(
                CheckoutClaimState.Completed,
                existingOrderId,
                "Đơn hàng đã được ghi nhận trước đó.");
        }

        if (attempt == null)
        {
            attempt = CreateAttempt(command, now);
            context.CheckoutAttempts.Add(attempt);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CheckoutClaimResult(
                CheckoutClaimState.Acquired,
                null,
                "Đã khóa phiên checkout.");
        }

        if (attempt.CustomerId != command.CustomerId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CheckoutClaimResult(
                CheckoutClaimState.Conflict,
                null,
                "Khóa checkout không thuộc tài khoản hiện tại.");
        }

        bool sameRequest = string.Equals(
            attempt.RequestHash,
            command.RequestHash,
            StringComparison.Ordinal);

        if (attempt.Status
                == CheckoutAttemptStatuses.Completed
            && attempt.OrderId.HasValue)
        {
            bool orderStillExists =
                await context.Orders.AnyAsync(
                    current =>
                        current.OrderId
                            == attempt.OrderId.Value
                        && current.CustomerId
                            == command.CustomerId,
                    cancellationToken);

            if (orderStillExists)
            {
                await transaction.CommitAsync(
                    cancellationToken);
                return new CheckoutClaimResult(
                    CheckoutClaimState.Completed,
                    attempt.OrderId,
                    "Đơn hàng đã được ghi nhận trước đó.");
            }

            attempt.Status =
                CheckoutAttemptStatuses.Failed;
            attempt.OrderId = null;
        }

        bool leaseActive =
            attempt.Status
                == CheckoutAttemptStatuses.Processing
            && attempt.ExpiresAt > now;

        if (leaseActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CheckoutClaimResult(
                CheckoutClaimState.InProgress,
                attempt.OrderId,
                "Yêu cầu đặt hàng đang được xử lý.");
        }

        if (!sameRequest
            && attempt.Status
                != CheckoutAttemptStatuses.Failed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CheckoutClaimResult(
                CheckoutClaimState.Conflict,
                null,
                "Nội dung checkout đã thay đổi. Vui lòng tải lại trang trước khi đặt hàng.");
        }

        attempt.RequestHash = command.RequestHash;
        attempt.SelectedAddressId =
            command.SelectedAddressId;
        attempt.PaymentMethod =
            NormalizePaymentMethod(
                command.PaymentMethod);
        attempt.Status =
            CheckoutAttemptStatuses.Processing;
        attempt.OrderId = null;
        attempt.UpdatedAt = now;
        attempt.ExpiresAt = now.Add(ProcessingLease);
        attempt.FailureReason = null;

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CheckoutClaimResult(
            CheckoutClaimState.Acquired,
            null,
            "Đã khóa lại phiên checkout.");
    }

    private async Task<CheckoutClaimResult> ResolveRaceAsync(
        CheckoutClaimCommand command,
        CancellationToken cancellationToken)
    {
        await using var context =
            new ApplicationDbContext(_options);

        int? orderId = await context.Orders
            .Where(current =>
                current.CustomerId == command.CustomerId
                && current.CheckoutIdempotencyKey
                    == command.IdempotencyKey)
            .Select(current => (int?)current.OrderId)
            .FirstOrDefaultAsync(cancellationToken);

        if (orderId.HasValue)
        {
            return new CheckoutClaimResult(
                CheckoutClaimState.Completed,
                orderId,
                "Đơn hàng đã được ghi nhận trước đó.");
        }

        CheckoutAttempts? attempt =
            await context.CheckoutAttempts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    current =>
                        current.IdempotencyKey
                            == command.IdempotencyKey,
                    cancellationToken);

        if (attempt == null)
        {
            return new CheckoutClaimResult(
                CheckoutClaimState.Conflict,
                null,
                "Không thể khóa phiên checkout. Vui lòng tải lại trang.");
        }

        if (!string.Equals(
                attempt.RequestHash,
                command.RequestHash,
                StringComparison.Ordinal))
        {
            return new CheckoutClaimResult(
                CheckoutClaimState.Conflict,
                null,
                "Nội dung checkout không khớp với phiên đã gửi.");
        }

        return new CheckoutClaimResult(
            CheckoutClaimState.InProgress,
            attempt.OrderId,
            "Yêu cầu đặt hàng đang được xử lý.");
    }

    private static CheckoutAttempts CreateAttempt(
        CheckoutClaimCommand command,
        DateTime now) =>
        new()
        {
            CustomerId = command.CustomerId,
            IdempotencyKey = command.IdempotencyKey,
            RequestHash = command.RequestHash,
            Status = CheckoutAttemptStatuses.Processing,
            SelectedAddressId =
                command.SelectedAddressId,
            PaymentMethod =
                NormalizePaymentMethod(
                    command.PaymentMethod),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.Add(ProcessingLease)
        };

    private static string NormalizePaymentMethod(
        string? method)
    {
        string value =
            method?.Trim().ToUpperInvariant()
            ?? string.Empty;

        return value.Length <= 30
            ? value
            : value[..30];
    }

    private static string? Truncate(
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
