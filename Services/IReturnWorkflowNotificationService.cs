using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed record ReturnWorkflowNotification(
    string Kind,
    int OrderId,
    string CustomerEmail,
    string CustomerName,
    string AdminNote);

public interface IReturnWorkflowNotificationService
{
    Task SendAsync(
        ReturnWorkflowNotification notification,
        CancellationToken cancellationToken = default);
}
