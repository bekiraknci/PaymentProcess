using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Infrastructure.Persistence.Configurations;

internal sealed class PaymentStatusHistoryConfiguration : IEntityTypeConfiguration<PaymentStatusHistory>
{
    public void Configure(EntityTypeBuilder<PaymentStatusHistory> builder)
    {
        builder.ToTable("PaymentStatusHistory");

        builder.HasKey(h => h.Id).IsClustered(false);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.HasIndex(h => new { h.ChangedAt, h.Id })
            .IsClustered()
            .HasDatabaseName("CX_PaymentStatusHistory_ChangedAt_Id");

        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.Reason).HasMaxLength(Payment.FailureReasonMaxLength);
        builder.Property(h => h.ChangedAt).IsRequired();
        builder.Property(h => h.ChangedBy).HasMaxLength(AuditConfiguration.ActorMaxLength);

        builder.HasIndex(h => new { h.PaymentId, h.ChangedAt })
            .HasDatabaseName("IX_PaymentStatusHistory_PaymentId_ChangedAt");
    }
}
