using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PaymentSystem.Application.Abstractions.Messaging;

namespace PaymentSystem.Infrastructure.Processing;

internal sealed class ChannelPaymentQueue : IPaymentQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });

    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    public ValueTask EnqueueAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!_queued.TryAdd(paymentId, 0))
        {
            return ValueTask.CompletedTask;
        }

        return _channel.Writer.WriteAsync(paymentId, cancellationToken);
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
