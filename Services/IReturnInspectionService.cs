using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed class ReturnInspectionQueueItem
{
    public int ReturnId { get; init; }

    public int OrderId { get; init; }

    public string CustomerName { get; init; }
        = string.Empty;

    public string ReturnStatus { get; init; }
        = string.Empty;

    public DateTime CreatedAt { get; init; }

    public bool IsFinalized { get; init; }

    public string? Decision { get; init; }

    public decimal? EligibleRefundAmount { get; init; }
}

public sealed class ReturnInspectionLineViewModel
{
    public int OrderDetailId { get; init; }

    public string ProductName { get; init; }
        = string.Empty;

    public string VariantName { get; init; }
        = string.Empty;

    public string VariantCode { get; init; }
        = string.Empty;

    public int ExpectedQuantity { get; init; }

    public int ReceivedQuantity { get; init; }

    public int ApprovedQuantity { get; init; }

    public string ConditionCode { get; init; }
        = string.Empty;

    public string Disposition { get; init; }
        = string.Empty;

    public decimal UnitAmount { get; init; }

    public decimal EligibleAmount { get; init; }

    public string? Note { get; init; }
}

public sealed class ReturnInspectionDetailsViewModel
{
    public int ReturnId { get; init; }

    public int OrderId { get; init; }

    public string CustomerName { get; init; }
        = string.Empty;

    public string CustomerEmail { get; init; }
        = string.Empty;

    public string ReturnReason { get; init; }
        = string.Empty;

    public string? ReturnDescription { get; init; }

    public string ReturnStatus { get; init; }
        = string.Empty;

    public decimal OriginalOrderAmount { get; init; }

    public bool IsFinalized { get; init; }

    public string? Decision { get; init; }

    public decimal EligibleRefundAmount { get; init; }

    public string? SummaryNote { get; init; }

    public DateTime? InspectedAt { get; init; }

    public string? InspectedBy { get; init; }

    public IReadOnlyList<ReturnInspectionLineViewModel> Lines
        { get; init; }
        = Array.Empty<ReturnInspectionLineViewModel>();
}

public sealed class ReturnInspectionLineInput
{
    public int OrderDetailId { get; set; }

    public int ReceivedQuantity { get; set; }

    public int ApprovedQuantity { get; set; }

    public string? ConditionCode { get; set; }

    public string? Disposition { get; set; }

    public string? Note { get; set; }
}

public sealed class ReturnInspectionFinalizeInput
{
    public int ReturnId { get; set; }

    public string? SummaryNote { get; set; }

    public List<ReturnInspectionLineInput> Lines
        { get; set; }
        = new();
}

public sealed record ReturnInspectionFinalizeResult(
    bool Success,
    bool AlreadyFinalized,
    bool RequiresReview,
    string Message,
    int ReturnId,
    int OrderId,
    string? Decision = null,
    decimal EligibleRefundAmount = 0m,
    string? CustomerEmail = null,
    string? CustomerName = null);

public interface IReturnInspectionService
{
    Task<IReadOnlyList<ReturnInspectionQueueItem>>
        GetQueueAsync(
            CancellationToken cancellationToken = default);

    Task<ReturnInspectionDetailsViewModel?>
        GetDetailsAsync(
            int returnId,
            CancellationToken cancellationToken = default);

    Task<ReturnInspectionFinalizeResult>
        FinalizeAsync(
            ReturnInspectionFinalizeInput input,
            string actor,
            CancellationToken cancellationToken = default);
}
