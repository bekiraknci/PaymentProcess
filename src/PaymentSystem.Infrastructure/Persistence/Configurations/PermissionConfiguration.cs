using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain.Users;
using PaymentSystem.Infrastructure.Persistence.Seed;

namespace PaymentSystem.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Name).HasName("PK_Permissions");

        builder.Property(p => p.Name).HasMaxLength(Permission.NameMaxLength).IsUnicode(false);
        builder.Property(p => p.Description).HasMaxLength(Permission.DescriptionMaxLength).IsRequired();

        builder.HasData(SeedData.Permissions());
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", t =>
            t.HasCheckConstraint("CK_RolePermissions_Role", "[Role] IN (N'Admin', N'User')"));

        builder.HasKey(rp => new { rp.Role, rp.PermissionName }).HasName("PK_RolePermissions");

        builder.Property(rp => rp.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(rp => rp.PermissionName).HasMaxLength(Permission.NameMaxLength).IsUnicode(false);

        builder.HasOne<Permission>()
            .WithMany()
            .HasForeignKey(rp => rp.PermissionName)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(SeedData.RolePermissions());
    }
}
