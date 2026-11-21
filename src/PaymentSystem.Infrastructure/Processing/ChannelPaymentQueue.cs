using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Messaging;
using PaymentSystem.Application.Payments.Processing;

namespace PaymentSystem.Infrastructure.Processing;

internal sealed class ChannelPaymentQueue : IPaymentQueue
{
    private readonly Channel<Guid> _channel;
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();
    private readonly ILogger<ChannelPaymentQueue> _logger;

    public ChannelPaymentQueue(IOptions<PaymentProcessingOptions> options, ILogger<ChannelPaymentQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!_queued.TryAdd(paymentId, 0))
        {
            return ValueTask.CompletedTask;
        }

        if (!_channel.Writer.TryWrite(paymentId))
        {
            _queued.TryRemove(paymentId, out _);
            _logger.LogWarning("Payment queue is full; payment {PaymentId} will be picked up by the database poll.", paymentId);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<Guid> DequeueAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var paymentId in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            _queued.TryRemove(paymentId, out _);
            yield return paymentId;
        }
    }
}
