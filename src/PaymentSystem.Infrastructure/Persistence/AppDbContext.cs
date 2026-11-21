using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Domain.Common;
using PaymentSystem.Domain.Payments;
using PaymentSystem.Domain.Providers;
using PaymentSystem.Domain.Users;

namespace PaymentSystem.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    private const int SqlUniqueIndexViolation = 2601;
    private const int SqlUniqueConstraintViolation = 2627;

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentStatusHistory> PaymentStatusHistory => Set<PaymentStatusHistory>();
    public DbSet<PaymentProvider> PaymentProviders => Set<PaymentProvider>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        int result;
        try
        {
            result = await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            ChangeTracker.Clear();
            throw new ConcurrencyConflictException("The entity was modified by another process.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: SqlUniqueIndexViolation or SqlUniqueConstraintViolation })
        {
            ChangeTracker.Clear();
            throw new UniqueConstraintViolationException("A unique constraint was violated.", ex);
        }

        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            entry.Entity.ClearDomainEvents();
        }

        return result;
    }
}
