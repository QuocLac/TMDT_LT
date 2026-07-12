using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed record ReturnIntakeCommand(
    int OrderId,
    int CustomerId,
    string? Reason,
    string? Description,
    string? BankCode,
    string? AccountNumber,
    string? AccountName,
    IReadOnlyList<IFormFile> Files);

public sealed record ReturnIntakeResult(
    bool Success,
    bool AlreadyExists,
    string Message,
    int? ReturnId = null,
    bool PaymentStateRepaired = false);

public interface IReturnIntakeService
{
    Task<ReturnIntakeResult> SubmitAsync(
        ReturnIntakeCommand command,
        CancellationToken cancellationToken = default);
}
