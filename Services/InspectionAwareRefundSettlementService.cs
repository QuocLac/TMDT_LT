using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class InspectionAwareRefundSettlementService
    : IRefundSettlementService
{
    private readonly RefundSettlementService _inner;
    private readonly ApplicationDbContext _context;

    public InspectionAwareRefundSettlementService(
        RefundSettlementService inner,
        ApplicationDbContext context)
    {
        _inner = inner;
        _context = context;
    }

    public async Task<RefundSettlementResult>
        SettleReturnAsync(
            ReturnRefundCommand command,
            CancellationToken cancellationToken = default)
    {
        ReturnInspections? inspection =
            await _context.ReturnInspections
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    current =>
                        current.ReturnId
                            == command.ReturnId,
                    cancellationToken);

        if (inspection == null
            || inspection.Status
                != ReturnInspectionStatuses.Completed)
        {
            return new RefundSettlementResult(
                false,
                false,
                false,
                "Hồ sơ chưa có kết quả kiểm định hoàn tất. Không thể bắt đầu hoàn tiền.");
        }

        if (inspection.Decision
            == ReturnInspectionDecisions.Reject)
        {
            return new RefundSettlementResult(
                false,
                false,
                false,
                "Kết quả kiểm định đã từ chối hoàn tiền.");
        }

        if (inspection.Decision
            == ReturnInspectionDecisions.ManualReview)
        {
            return new RefundSettlementResult(
                false,
                false,
                true,
                "Kết quả kiểm định chỉ chấp nhận một phần hoặc có sai lệch. Cần đối soát thủ công; không gọi provider tự động.");
        }

        if (inspection.Decision
            != ReturnInspectionDecisions.FullRefund)
        {
            return new RefundSettlementResult(
                false,
                false,
                true,
                "Kết quả kiểm định không xác định. Cần đối soát.");
        }

        // Không tin cờ IsProductIntact từ trình duyệt.
        // Kết quả kiểm định đã lưu mới là nguồn quyết định.
        ReturnRefundCommand trustedCommand =
            command with
            {
                IsProductIntact = true
            };

        return await _inner.SettleReturnAsync(
            trustedCommand,
            cancellationToken);
    }

    public Task<RefundSettlementResult>
        SettleCancellationAsync(
            OrderCancellationCommand command,
            CancellationToken cancellationToken = default) =>
        _inner.SettleCancellationAsync(
            command,
            cancellationToken);

    public Task<RefundSettlementResult>
        ReconcileRefundAsync(
            RefundReconciliationCommand command,
            CancellationToken cancellationToken = default) =>
        _inner.ReconcileRefundAsync(
            command,
            cancellationToken);
}
