using Atlas.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Audit.Infrastructure;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("audit");

        builder.Entity<AuditEntry>(b =>
        {
            b.ToTable("AuditEntries");
            b.HasKey(a => a.Id);
            b.Property(a => a.ActorDisplay).HasMaxLength(256).IsRequired();
            b.Property(a => a.Action).HasMaxLength(256).IsRequired();
            b.Property(a => a.ResourceType).HasMaxLength(128).IsRequired();
            b.Property(a => a.ResourceId).HasMaxLength(128).IsRequired();
            b.Property(a => a.IpAddress).HasMaxLength(64);
            b.HasIndex(a => a.OrganizationId);
            b.HasIndex(a => new { a.ResourceType, a.ResourceId });
            b.HasIndex(a => a.CreatedAtUtc);
        });
    }

    /// <summary>
    /// Enforces append-only at the DbContext level: rejects any attempt to
    /// modify or delete an AuditEntry that reaches this DbContext, even via
    /// a bug elsewhere in the app. This is a real guard, not just a missing
    /// setter — though a superuser issuing raw SQL against Postgres directly
    /// bypasses it, which is why production Postgres role grants should also
    /// deny UPDATE/DELETE on the audit.AuditEntries table (see docs/security.md TODO).
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardAppendOnly()
    {
        var illegal = ChangeTracker.Entries<AuditEntry>()
            .Any(e => e.State == EntityState.Modified || e.State == EntityState.Deleted);
        if (illegal)
            throw new InvalidOperationException("AuditEntry records are append-only and cannot be modified or deleted.");
    }
}
