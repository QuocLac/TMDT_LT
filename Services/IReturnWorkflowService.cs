using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public static class ReturnWorkflowNotificationKinds
{
    public const string None = "None";

    public const string Accepted = "Accepted";

    public const string Inspecting = "Inspecting";

    public const string Rejected = "Rejected";
}

public sealed record ReturnWorkflowCommand(
    int ReturnId,
    string? NewStatus,
    string? AdminNote,
    string Actor);

public sealed record ReturnWorkflowResult(
    bool Success,
    bool AlreadyApplied,
    string Message,
    int? ReturnId = null,
    int? OrderId = null,
    string? CustomerEmail = null,
    string? CustomerName = null,
    string NotificationKind =
        ReturnWorkflowNotificationKinds.None,
    bool PaymentStateRepaired = false,
    bool OrderStateRepaired = false);

public interface IReturnWorkflowService
{
    Task<ReturnWorkflowResult> TransitionAsync(
        ReturnWorkflowCommand command,
        CancellationToken cancellationToken = default);
}
