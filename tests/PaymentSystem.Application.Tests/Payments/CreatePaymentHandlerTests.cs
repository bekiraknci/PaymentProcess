using FluentAssertions;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Common.Results;
using PaymentSystem.Application.Payments.CreatePayment;
using PaymentSystem.Application.Tests.Fakes;
using PaymentSystem.Domain.Payments;
using PaymentSystem.Domain.Providers;

namespace PaymentSystem.Application.Tests.Payments;

public sealed class CreatePaymentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly InMemoryPaymentRepository _payments = new();
    private readonly InMemoryProviderRepository _providers = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingPaymentQueue _queue = new();
    private readonly PaymentProvider _provider;

    public CreatePaymentHandlerTests()
    {
        _provider = PaymentProvider.Create("FAKEPAY", "FakePay", null, ["USD", "EUR"], isActive: true, Now);
        _providers.Add(_provider);
    }

    private CreatePaymentHandler CreateHandler(Guid? userId = null) => new(
        new CreatePaymentValidator(),
        _payments,
        _providers,
        _unitOfWork,
        _queue,
        new FakeCurrentUser(userId ?? _userId),
        new FixedTimeProvider(Now));

    private CreatePaymentCommand Command(decimal amount = 100m, string currency = "USD", string? key = "key-1") =>
        new(_provider.Id, amount, currency, key);

    [Fact]
    public async Task Creates_pending_payment_and_enqueues_it()
    {
        var result = await CreateHandler().HandleAsync(Command(key: null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Replayed.Should().BeFalse();
        result.Value.Payment.Status.Should().Be(PaymentStatus.Pending);
        result.Value.Payment.UserId.Should().Be(_userId);
        _payments.Items.Should().ContainSingle();
        _queue.Enqueued.Should().Equal(result.Value.Payment.Id);
    }

    [Fact]
    public async Task Same_key_and_body_returns_original_payment_without_creating_a_new_one()
    {
        var handler = CreateHandler();

        var first = await handler.HandleAsync(Command(), CancellationToken.None);
        var second = await handler.HandleAsync(Command(), CancellationToken.None);

        second.IsSuccess.Should().BeTrue();
        second.Value.Replayed.Should().BeTrue();
        second.Value.Payment.Id.Should().Be(first.Value.Payment.Id);
        _payments.Items.Should().ContainSingle();
        _queue.Enqueued.Should().ContainSingle();
    }

    [Fact]
    public async Task Same_key_with_different_body_is_a_conflict()
    {
        var handler = CreateHandler();
        await handler.HandleAsync(Command(amount: 100m), CancellationToken.None);

        var result = await handler.HandleAsync(Command(amount: 250m), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Type.Should().Be(ErrorType.Conflict);
        _payments.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Idempotency_keys_are_scoped_per_user()
    {
        await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        var otherUser = await CreateHandler(Guid.NewGuid()).HandleAsync(Command(), CancellationToken.None);

        otherUser.IsSuccess.Should().BeTrue();
        otherUser.Value.Replayed.Should().BeFalse();
        _payments.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Concurrent_insert_with_same_key_returns_the_winning_payment()
    {
        var winner = Payment.Create(_userId, _provider.Id, Money.Create(100m, "USD"), "key-1", Command().ComputeRequestHash(), Now);
        _unitOfWork.BeforeSave = () =>
        {
            _payments.Items.RemoveAll(p => p.Id != winner.Id);
            _payments.Items.Add(winner);
            return new UniqueConstraintViolationException("duplicate", new Exception());
        };

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Replayed.Should().BeTrue();
        result.Value.Payment.Id.Should().Be(winner.Id);
        _queue.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_provider_returns_not_found()
    {
        var result = await CreateHandler().HandleAsync(Command() with { ProviderId = Guid.NewGuid() }, CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Inactive_provider_is_a_validation_error()
    {
        _provider.Deactivate();

        var result = await CreateHandler().HandleAsync(Command(), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.ValidationErrors.Should().ContainKey("providerId");
    }

    [Fact]
    public async Task Currency_not_supported_by_provider_is_a_validation_error()
    {
        var result = await CreateHandler().HandleAsync(Command(currency: "TRY"), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.ValidationErrors.Should().ContainKey("currency");
    }

    [Theory]
    [InlineData(0, "USD", "amount")]
    [InlineData(-5, "USD", "amount")]
    [InlineData(10.123, "USD", "amount")]
    [InlineData(10, "XYZ", "currency")]
    public async Task Invalid_input_returns_field_errors(decimal amount, string currency, string field)
    {
        var result = await CreateHandler().HandleAsync(Command(amount, currency), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.ValidationErrors.Should().ContainKey(field);
        _payments.Items.Should().BeEmpty();
    }
}
