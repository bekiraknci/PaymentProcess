using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Domain.Common;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Infrastructure.Persistence.Interceptors;

public sealed class AuditableEntityInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public const string SystemActor = "system";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAudit(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var actor = ResolveActor();

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).CurrentValue = actor;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(IAuditableEntity.UpdatedAt)).CurrentValue = now;
                    entry.Property(nameof(IAuditableEntity.UpdatedBy)).CurrentValue = actor;
                    break;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<PaymentStatusHistory>())
        {
            if (entry.State == EntityState.Added && entry.Entity.ChangedBy is null)
            {
                entry.Property(nameof(PaymentStatusHistory.ChangedBy)).CurrentValue = actor;
            }
        }
    }

    private string ResolveActor() =>
        currentUser.IsAuthenticated ? currentUser.Email ?? currentUser.Id.ToString() : SystemActor;
}
