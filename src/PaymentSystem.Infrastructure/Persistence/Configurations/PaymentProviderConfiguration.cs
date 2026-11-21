using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain.Providers;
using PaymentSystem.Infrastructure.Persistence.Seed;

namespace PaymentSystem.Infrastructure.Persistence.Configurations;

internal sealed class PaymentProviderConfiguration : IEntityTypeConfiguration<PaymentProvider>
{
    public void Configure(EntityTypeBuilder<PaymentProvider> builder)
    {
        builder.ToTable("PaymentProviders");
        builder.ConfigureAggregate();

        builder.Property(p => p.Code).HasMaxLength(PaymentProvider.CodeMaxLength).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(PaymentProvider.NameMaxLength).IsRequired();
        builder.Property(p => p.BaseUrl).HasMaxLength(PaymentProvider.BaseUrlMaxLength);
        builder.Property(p => p.IsActive).IsRequired();

        builder.Ignore(p => p.SupportedCurrencies);
        builder.Property<string>("_supportedCurrencies")
            .HasColumnName("SupportedCurrencies")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(p => p.Code)
            .IsUnique()
            .HasDatabaseName("UX_PaymentProviders_Code");

        builder.HasData(SeedData.Providers());
    }
}
