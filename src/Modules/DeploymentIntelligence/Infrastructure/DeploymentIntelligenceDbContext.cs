using Atlas.Modules.DeploymentIntelligence.Domain;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.DeploymentIntelligence.Infrastructure;

public class DeploymentIntelligenceDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public DeploymentIntelligenceDbContext(DbContextOptions<DeploymentIntelligenceDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Deployment> Deployments => Set<Deployment>();

    /// <summary>
    /// Transactional outbox. Written in the same SaveChanges as the deployment
    /// row, published by OutboxRelayService. Not tenant-filtered: it is
    /// infrastructure state, and the relay must see every pending message.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("deploymentintelligence");

        builder.Entity<Deployment>(b =>
        {
            b.ToTable("Deployments");
            b.HasKey(d => d.Id);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.Property(d => d.Version).HasMaxLength(128).IsRequired();
            b.Property(d => d.Environment).HasMaxLength(64).IsRequired();
            b.Property(d => d.CommitSha).HasMaxLength(64).IsRequired();
            b.Property(d => d.Author).HasMaxLength(256);
            b.HasIndex(d => new { d.OrganizationId, d.ServiceId, d.DeployedAtUtc });
            b.HasQueryFilter(d => !_tenantContext.HasOrganization || d.OrganizationId == _tenantContext.CurrentOrganizationId);
        });

        builder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(m => m.Id);
            // Insert-once/update-once infrastructure rows: leasing (NextAttemptAtUtc)
            // rather than a row version, so no token column is needed.
            b.Ignore(x => x.RowVersion);
            b.Property(m => m.Topic).HasMaxLength(256).IsRequired();
            b.Property(m => m.EventType).HasMaxLength(256).IsRequired();
            b.Property(m => m.Producer).HasMaxLength(256).IsRequired();
            b.Property(m => m.PayloadJson).IsRequired();
            b.Property(m => m.LastError).HasMaxLength(2000);
            // The relay's claim query: pending rows that are due, oldest first.
            b.HasIndex(m => new { m.SentAtUtc, m.AbandonedAtUtc, m.NextAttemptAtUtc });
        });
    }
}
