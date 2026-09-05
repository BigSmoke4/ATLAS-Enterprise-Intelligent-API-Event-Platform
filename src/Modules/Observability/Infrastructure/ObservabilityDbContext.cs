using Atlas.Modules.Observability.Domain;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Observability.Infrastructure;

public class ObservabilityDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public ObservabilityDbContext(DbContextOptions<ObservabilityDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<ServiceLevelObjective> Slos => Set<ServiceLevelObjective>();
    public DbSet<MetricSample> MetricSamples => Set<MetricSample>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("observability");

        builder.Entity<ServiceLevelObjective>(b =>
        {
            b.ToTable("ServiceLevelObjectives");
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(s => new { s.OrganizationId, s.ServiceId });
            b.HasQueryFilter(s => !_tenantContext.HasOrganization || s.OrganizationId == _tenantContext.CurrentOrganizationId);
        });

        builder.Entity<MetricSample>(b =>
        {
            b.ToTable("MetricSamples");
            b.HasKey(m => m.Id);
            // High write volume, queried by service+type+time — index accordingly.
            b.HasIndex(m => new { m.OrganizationId, m.ServiceId, m.MetricType, m.RecordedAtUtc });
            b.HasQueryFilter(m => !_tenantContext.HasOrganization || m.OrganizationId == _tenantContext.CurrentOrganizationId);
        });
    }
}
