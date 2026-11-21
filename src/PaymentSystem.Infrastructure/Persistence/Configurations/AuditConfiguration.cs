using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain.Common;

namespace PaymentSystem.Infrastructure.Persistence.Configurations;

internal static class AuditConfiguration
{
    public const int ActorMaxLength = 256;

    public static void ConfigureAggregate<T>(this EntityTypeBuilder<T> builder) where T : AggregateRoot
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.CreatedBy).HasMaxLength(ActorMaxLength);
        builder.Property(e => e.UpdatedBy).HasMaxLength(ActorMaxLength);

        builder.Ignore(e => e.DomainEvents);
    }
}
