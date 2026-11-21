using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Messaging;
using PaymentSystem.Application.Payments.Processing;

namespace PaymentSystem.Infrastructure.Processing;

internal sealed class PaymentProcessingWorker(
    IPaymentQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        logger.LogInformation(
            "Payment processing worker started with {WorkerCount} consumer(s); safety-net poll every {Interval}s.",
            settings.WorkerCount, settings.PollIntervalSeconds);

        await RecoverAsync(stoppingToken);

        var consumers = Enumerable.Range(1, settings.WorkerCount)
            .Select(n => ConsumeAsync(n, stoppingToken))
            .Append(PollAsync(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken));

        await Task.WhenAll(consumers);
    }

    private async Task ConsumeAsync(int consumerId, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var paymentId in queue.DequeueAllAsync(stoppingToken))
            {
                await ProcessAsync(consumerId, paymentId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessAsync(int consumerId, Guid paymentId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ProcessPaymentHandler>();
            await handler.HandleAsync(paymentId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Shutdown requested while processing payment {PaymentId}; its lease will expire and it will be recovered.", paymentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Consumer {ConsumerId} failed to process payment {PaymentId}.", consumerId, paymentId);
        }
    }

    private async Task PollAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RecoverAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<RecoverPaymentsHandler>();
            var pendingIds = await handler.HandleAsync(stoppingToken);

            foreach (var id in pendingIds)
            {
                await queue.EnqueueAsync(id, stoppingToken);
            }

            if (pendingIds.Count > 0)
            {
                logger.LogInformation("Queued {Count} pending payment(s) from the database.", pendingIds.Count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payment recovery pass failed; it will run again on the next tick.");
        }
    }
}
