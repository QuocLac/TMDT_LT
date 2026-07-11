using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed class UnpaidOrderExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<UnpaidOrderExpirationOptions> _options;
    private readonly ILogger<UnpaidOrderExpirationWorker> _logger;

    public UnpaidOrderExpirationWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<UnpaidOrderExpirationOptions> options,
        ILogger<UnpaidOrderExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelay = Math.Max(0, _options.CurrentValue.InitialDelaySeconds);
        if (initialDelay > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(initialDelay), stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            if (options.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider
                        .GetRequiredService<IUnpaidOrderExpirationService>();
                    var expiredCount = await service.ExpireDueOrdersAsync(stoppingToken);

                    if (expiredCount > 0)
                    {
                        _logger.LogInformation(
                            "Đã tự động hết hạn {ExpiredCount} đơn chưa thanh toán.",
                            expiredCount);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi worker hết hạn đơn chưa thanh toán.");
                }
            }

            var intervalSeconds = Math.Max(15, options.ScanIntervalSeconds);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
