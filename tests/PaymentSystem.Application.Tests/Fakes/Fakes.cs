using System.Runtime.CompilerServices;
using PaymentSystem.Application.Abstractions.Messaging;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Abstractions.Providers;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Domain.Payments;
using PaymentSystem.Domain.Providers;

namespace PaymentSystem.Application.Tests.Fakes;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeCurrentUser(Guid id, bool isAdmin = false) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid Id { get; } = id;
    public string? Email => $"{Id:N}@test.com";
    public bool IsAdmin { get; } = isAdmin;
    public bool HasPermission(string permission) => true;
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Func<Exception?>? BeforeSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (BeforeSave?.Invoke() is { } exception)
        {
            BeforeSave = null;
            throw exception;
        }

        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class InMemoryPaymentRepository : IPaymentRepository
{
    public List<Payment> Items { get; } = [];

    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<Payment?> GetByIdempotencyKeyAsync(Guid userId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.UserId == userId && p.IdempotencyKey == idempotencyKey));

    public Task<IReadOnlyList<Guid>> GetPendingIdsAsync(int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(Items.Where(p => p.Status == PaymentStatus.Pending).Select(p => p.Id).Take(take).ToList());

    public Task<IReadOnlyList<Payment>> GetExpiredProcessingAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Payment>>(Items.Where(p => p.IsLeaseExpired(now)).Take(take).ToList());

    public void Add(Payment payment) => Items.Add(payment);
}

internal sealed class InMemoryProviderRepository : IPaymentProviderRepository
{
    public List<PaymentProvider> Items { get; } = [];

    public Task<PaymentProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(p => p.Code == code.Trim().ToUpperInvariant()));

    public Task<IReadOnlyList<PaymentProvider>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PaymentProvider>>(Items.ToList());

    public void Add(PaymentProvider provider) => Items.Add(provider);
}

internal sealed class RecordingPaymentQueue : IPaymentQueue
{
    public List<Guid> Enqueued { get; } = [];

    public ValueTask EnqueueAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        Enqueued.Add(paymentId);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<Guid> DequeueAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var id in Enqueued)
        {
            yield return id;
        }

        await Task.CompletedTask;
    }
}

internal sealed class StubProviderClient(ProviderChargeResult result) : IPaymentProviderClient
{
    public int Calls { get; private set; }

    public ProviderChargeResult Result { get; set; } = result;

    public Task<ProviderChargeResult> ChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
