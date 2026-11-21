namespace PaymentSystem.Application.Abstractions.Persistence;

public sealed class UniqueConstraintViolationException(string message, Exception innerException)
    : Exception(message, innerException);

public sealed class ConcurrencyConflictException(string message, Exception innerException)
    : Exception(message, innerException);
