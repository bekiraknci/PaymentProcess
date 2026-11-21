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

        builder.HasKey(p => p.Id).IsClustered(false);
        builder.HasIndex(p => new { p.CreatedAt, p.Id })
            .IsClustered()
            .HasDatabaseName("CX_Payments_CreatedAt_Id");

        builder.HasIndex(p => new { p.UserId, p.Status, p.CreatedAt })
            .HasDatabaseName("IX_Payments_UserId_Status_CreatedAt");

        builder.HasIndex(p => new { p.UserId, p.CreatedAt })
            .HasDatabaseName("IX_Payments_UserId_CreatedAt");

        builder.HasIndex(p => new { p.Status, p.NextAttemptAt })
            .HasDatabaseName("IX_Payments_Status_NextAttemptAt");

        builder.HasIndex(p => new { p.Status, p.CreatedAt })
            .HasDatabaseName("IX_Payments_Status_CreatedAt");

        builder.HasIndex(p => p.LockedUntil)
            .HasFilter("[Status] = N'Processing'")
            .HasDatabaseName("IX_Payments_Processing_LockedUntil");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0");
            t.HasCheckConstraint("CK_Payments_Currency_Format", "[Currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'");
            t.HasCheckConstraint("CK_Payments_Status", "[Status] IN (N'Pending', N'Processing', N'Completed', N'Failed')");
            t.HasCheckConstraint("CK_Payments_AttemptCount_NonNegative", "[AttemptCount] >= 0");
            t.HasCheckConstraint("CK_Payments_Completed_HasTransaction", "[Status] <> N'Completed' OR [ProviderTransactionId] IS NOT NULL");
        });

        builder.HasIndex(p => new { p.ProviderId, p.CreatedAt })
            .IncludeProperties(p => p.Status)
            .HasDatabaseName("IX_Payments_ProviderId_CreatedAt");

        builder.HasIndex(p => new { p.UserId, p.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL")
            .HasDatabaseName("UX_Payments_UserId_IdempotencyKey");
    }
}
