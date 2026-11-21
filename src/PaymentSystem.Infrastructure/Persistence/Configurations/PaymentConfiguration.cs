using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain.Payments;
using PaymentSystem.Domain.Providers;
using PaymentSystem.Domain.Users;

namespace PaymentSystem.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.ConfigureAggregate();

        builder.OwnsOne(p => p.Money, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("Amount")
                .HasPrecision(18, 2)
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("Currency")
                .HasColumnType("char(3)")
                .HasMaxLength(3)
                .IsFixedLength()
                .IsRequired();
        });
        builder.Navigation(p => p.Money).IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.IdempotencyKey).HasMaxLength(Payment.IdempotencyKeyMaxLength);
        builder.Property(p => p.RequestHash).HasColumnType("char(64)").HasMaxLength(64).IsFixedLength();
        builder.Property(p => p.ProviderTransactionId).HasMaxLength(128);
        builder.Property(p => p.FailureReason).HasMaxLength(Payment.FailureReasonMaxLength);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PaymentProvider>()
            .WithMany()
            .HasForeignKey(p => p.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.History)
            .WithOne()
            .HasForeignKey(h => h.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.History)
            .HasField("_history")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => new { p.UserId, p.Status, p.CreatedAt })
            .HasDatabaseName("IX_Payments_UserId_Status_CreatedAt");

        builder.HasIndex(p => new { p.Status, p.NextAttemptAt })
            .HasDatabaseName("IX_Payments_Status_NextAttemptAt");

        builder.HasIndex(p => new { p.ProviderId, p.CreatedAt })
            .IncludeProperties(p => p.Status)
            .HasDatabaseName("IX_Payments_ProviderId_CreatedAt");

        builder.HasIndex(p => new { p.UserId, p.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL")
            .HasDatabaseName("UX_Payments_UserId_IdempotencyKey");
    }
}
