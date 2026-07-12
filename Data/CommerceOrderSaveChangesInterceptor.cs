using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Models;

namespace TMDT_LT.Data;

public sealed class CommerceOrderSaveChangesInterceptor
    : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CommerceOrderSaveChangesInterceptor(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyOrderSnapshots(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>>
        SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
    {
        ApplyOrderSnapshots(eventData.Context);
        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        CaptureCreatedOrderId(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        CaptureCreatedOrderId(eventData.Context);
        return base.SavedChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    private void ApplyOrderSnapshots(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        HttpContext? httpContext =
            _httpContextAccessor.HttpContext;

        string? idempotencyKey = httpContext?.Items[
            CheckoutIdempotencyConstants.KeyItemName
        ]?.ToString();

        string? voucherCode = httpContext?.Items[
            CheckoutIdempotencyConstants.VoucherItemName
        ]?.ToString();

        foreach (var entry in context.ChangeTracker
                     .Entries<Orders>()
                     .Where(current =>
                         current.State == EntityState.Added))
        {
            Orders order = entry.Entity;

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                order.CheckoutIdempotencyKey =
                    idempotencyKey.Trim();
            }

            string? normalizedVoucherCode =
                NormalizeVoucherCode(voucherCode);

            decimal subtotal = order.OrderDetails
                .Sum(detail =>
                    (detail.LineTotal ?? 0) > 0
                        ? detail.LineTotal ?? 0
                        : (detail.UnitPrice ?? 0)
                            * (detail.Quantity ?? 0));

            decimal shippingFee =
                Math.Max(0, order.ShippingFee ?? 0);
            decimal taxAmount =
                Math.Max(0, order.TaxAmount);

            decimal controllerTotal =
                Math.Max(
                    0,
                    order.TotalAmount
                    ?? order.GrandTotalAmount);

            if (subtotal <= 0 && controllerTotal > 0)
            {
                subtotal = Math.Max(
                    0,
                    controllerTotal - shippingFee - taxAmount);
            }

            decimal discountAmount = Math.Max(
                0,
                subtotal
                    + shippingFee
                    + taxAmount
                    - controllerTotal);

            decimal grandTotal = Math.Max(
                0,
                subtotal
                    - discountAmount
                    + shippingFee
                    + taxAmount);

            order.SubtotalAmount = RoundMoney(subtotal);
            order.DiscountAmount =
                RoundMoney(discountAmount);
            order.AppliedVoucherCode =
                discountAmount > 0
                    ? normalizedVoucherCode
                    : null;
            order.TaxAmount = RoundMoney(taxAmount);
            order.GrandTotalAmount =
                RoundMoney(grandTotal);

            // TotalAmount vẫn là trường tương thích chính của hệ thống.
            order.TotalAmount = order.GrandTotalAmount;
        }
    }

    private void CaptureCreatedOrderId(DbContext? context)
    {
        HttpContext? httpContext =
            _httpContextAccessor.HttpContext;

        if (context == null || httpContext == null)
        {
            return;
        }

        string? key = httpContext.Items[
            CheckoutIdempotencyConstants.KeyItemName
        ]?.ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        Orders? createdOrder = context.ChangeTracker
            .Entries<Orders>()
            .Select(current => current.Entity)
            .Where(current =>
                current.OrderId > 0
                && string.Equals(
                    current.CheckoutIdempotencyKey,
                    key,
                    StringComparison.Ordinal))
            .OrderByDescending(current => current.OrderId)
            .FirstOrDefault();

        if (createdOrder != null)
        {
            httpContext.Items[
                CheckoutIdempotencyConstants
                    .CreatedOrderIdItemName
            ] = createdOrder.OrderId;
        }
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);

    private static string? NormalizeVoucherCode(
        string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string normalized =
            code.Trim().ToUpperInvariant();

        return normalized.Length <= 20
            ? normalized
            : normalized[..20];
    }
}
