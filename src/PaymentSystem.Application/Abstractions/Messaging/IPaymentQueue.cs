namespace PaymentSystem.Application.Abstractions.Messaging;

public interface IPaymentQueue
{
    ValueTask EnqueueAsync(Guid paymentId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}
