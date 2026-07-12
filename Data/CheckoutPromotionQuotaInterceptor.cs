using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Models;

namespace TMDT_LT.Data;

/// <summary>
/// Bảo vệ claim voucher trong luồng Checkout/PlaceOrder.
///
/// Runtime model đánh dấu Promotions.UsedCount và CustomerWallet.Status là
/// concurrency token. Interceptor này bổ sung kiểm tra nghiệp vụ trước khi lưu
/// và đổi lỗi cạnh tranh quota thành thông báo có thể hiểu được ở checkout.
/// </summary>
public sealed class CheckoutPromotionQuotaInterceptor
    : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CheckoutPromotionQuotaInterceptor(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateCheckoutClaims(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>>
        SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
    {
        ValidateCheckoutClaims(eventData.Context);

        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    public override void SaveChangesFailed(
        DbContextErrorEventData eventData)
    {
        ThrowFriendlyConcurrencyError(
            eventData.Exception);

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ThrowFriendlyConcurrencyError(
            eventData.Exception);

        return base.SaveChangesFailedAsync(
            eventData,
            cancellationToken);
    }

    private void ValidateCheckoutClaims(
        DbContext? context)
    {
        if (context == null || !IsCheckoutPlaceOrder())
        {
            return;
        }

        DateTime now = DateTime.Now;

        foreach (var entry in context.ChangeTracker
                     .Entries<Promotions>()
                     .Where(current =>
                         current.State == EntityState.Modified))
        {
            var usedCountProperty =
                entry.Property(current =>
                    current.UsedCount);

            if (!usedCountProperty.IsModified)
            {
                continue;
            }

            int originalUsedCount =
                usedCountProperty.OriginalValue;
            int currentUsedCount =
                usedCountProperty.CurrentValue;

            if (currentUsedCount
                != originalUsedCount + 1)
            {
                throw new PromotionQuotaClaimException(
                    "Voucher chỉ được claim đúng một lượt cho mỗi lần đặt hàng.");
            }

            if (!entry.Entity.IsActive
                || entry.Entity.StartDate > now
                || entry.Entity.EndDate < now)
            {
                throw new PromotionQuotaClaimException(
                    "Voucher đã dừng hoặc không còn trong thời gian áp dụng.");
            }

            if (entry.Entity.UsageLimit <= 0
                || currentUsedCount
                    > entry.Entity.UsageLimit)
            {
                throw new PromotionQuotaClaimException(
                    "Voucher vừa hết lượt sử dụng. Vui lòng chọn mã khác.");
            }
        }

        foreach (var entry in context.ChangeTracker
                     .Entries<CustomerWallet>()
                     .Where(current =>
                         current.State == EntityState.Modified))
        {
            var statusProperty =
                entry.Property(current =>
                    current.Status);

            if (!statusProperty.IsModified)
            {
                continue;
            }

            int originalStatus =
                statusProperty.OriginalValue;
            int currentStatus =
                statusProperty.CurrentValue;

            if (originalStatus != 0
                || currentStatus != 1)
            {
                throw new PromotionQuotaClaimException(
                    "Voucher trong ví không còn ở trạng thái có thể sử dụng.");
            }

            if (!entry.Entity.UsedAt.HasValue)
            {
                throw new PromotionQuotaClaimException(
                    "Voucher chưa ghi nhận thời điểm sử dụng.");
            }
        }
    }

    private void ThrowFriendlyConcurrencyError(
        Exception exception)
    {
        if (!IsCheckoutPlaceOrder()
            || exception
                is not DbUpdateConcurrencyException
                    concurrencyException)
        {
            return;
        }

        bool promotionConflict =
            concurrencyException.Entries.Any(
                current =>
                    current.Entity is Promotions
                    || current.Entity
                        is CustomerWallet);

        if (!promotionConflict)
        {
            return;
        }

        // Không gắn inner exception vì CheckoutController hiện ưu tiên
        // InnerException.Message khi hiển thị lỗi.
        throw new PromotionQuotaClaimException(
            "Voucher vừa được một giao dịch khác sử dụng hoặc đã hết lượt. "
            + "Đơn hàng chưa được tạo và tồn kho chưa bị trừ. "
            + "Vui lòng tải lại trang checkout để chọn voucher khác.");
    }

    private bool IsCheckoutPlaceOrder()
    {
        HttpContext? httpContext =
            _httpContextAccessor.HttpContext;

        if (httpContext == null
            || !HttpMethods.IsPost(
                httpContext.Request.Method))
        {
            return false;
        }

        string? controller =
            httpContext.Request.RouteValues[
                "controller"
            ]?.ToString();
        string? action =
            httpContext.Request.RouteValues[
                "action"
            ]?.ToString();

        return string.Equals(
                controller,
                "Checkout",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                action,
                "PlaceOrder",
                StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class PromotionQuotaClaimException
    : InvalidOperationException
{
    public PromotionQuotaClaimException(
        string message)
        : base(message)
    {
    }
}
