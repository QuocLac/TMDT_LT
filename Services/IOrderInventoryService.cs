using System;
using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

/// <summary>
/// Cổng nghiệp vụ duy nhất để trừ/hoàn tồn kho theo đơn hàng.
/// Các phương thức chỉ thay đổi entity đang được DbContext theo dõi;
/// transaction và SaveChanges do application flow bên ngoài kiểm soát.
/// </summary>
public interface IOrderInventoryService
{
    Task<bool> DeductOrderStockAsync(
        int orderId,
        string reason,
        DateTime? occurredAt = null,
        CancellationToken cancellationToken = default);

    Task<bool> RestoreOrderStockAsync(
        int orderId,
        string reason,
        bool restoreFlashSaleSlots = true,
        DateTime? occurredAt = null,
        CancellationToken cancellationToken = default);
}
