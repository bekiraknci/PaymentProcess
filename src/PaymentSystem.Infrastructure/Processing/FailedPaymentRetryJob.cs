using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Payments.Retry;

namespace PaymentSystem.Infrastructure.Processing;

internal sealed class FailedPaymentRetryJob(
    IServiceScopeFactory scopeFactory,
    IOptions<RetryJobOptions> options,
    TimeProvider timeProvider,
    ILogger<FailedPaymentRetryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Failed payment retry job is disabled.");
            return;
        }

        logger.LogInformation("Failed payment retry job started; runs every {Interval}.", settings.Interval);

        using var timer = new PeriodicTimer(settings.Interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<RetryDueFailedPaymentsHandler>();
            await handler.HandleAsync(stoppingToken);
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed payment retry tick failed; the job keeps running.");
        }
    }
}
