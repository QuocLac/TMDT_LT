using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public interface IUnpaidOrderExpirationService
{
    Task<int> ExpireDueOrdersAsync(CancellationToken cancellationToken = default);
}
