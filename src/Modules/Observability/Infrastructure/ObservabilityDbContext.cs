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

    /// <summary>One-minute live request telemetry roll-ups (see RequestTelemetryAggregate).</summary>
    public DbSet<RequestTelemetryAggregate> RequestTelemetryAggregates => Set<RequestTelemetryAggregate>();

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

        builder.Entity<RequestTelemetryAggregate>(b =>
        {
            b.ToTable("RequestTelemetryAggregates");
            b.HasKey(a => a.Id);
            b.Property(a => a.Route).HasMaxLength(512).IsRequired();
            b.Property(a => a.HttpMethod).HasMaxLength(10).IsRequired();
            b.Property(a => a.DeploymentVersion).HasMaxLength(128).IsRequired();
            // Fixed-bucket latency histogram as a PostgreSQL bigint[] — one row
            // per minute per route instead of one row per request.
            b.Property(a => a.DurationBucketCounts).HasColumnType("bigint[]").IsRequired();
            b.HasIndex(a => new { a.OrganizationId, a.WindowStartUtc });
            b.HasIndex(a => new { a.OrganizationId, a.ServiceId, a.WindowStartUtc });
            b.HasIndex(a => new { a.OrganizationId, a.ServiceId, a.Route, a.HttpMethod, a.DeploymentVersion, a.WindowStartUtc }).IsUnique();
            b.HasQueryFilter(a => !_tenantContext.HasOrganization || a.OrganizationId == _tenantContext.CurrentOrganizationId);
        });

        builder.Entity<MetricSample>(b =>
        {
            b.ToTable("MetricSamples");
            b.HasKey(m => m.Id);
            b.Property(m => m.DeploymentVersion).HasMaxLength(128);
            // High write volume, queried by service+type+time — index accordingly.
            b.HasIndex(m => new { m.OrganizationId, m.ServiceId, m.MetricType, m.RecordedAtUtc });
            b.HasIndex(m => new { m.OrganizationId, m.ServiceId, m.DeploymentVersion, m.RecordedAtUtc });
            b.HasQueryFilter(m => !_tenantContext.HasOrganization || m.OrganizationId == _tenantContext.CurrentOrganizationId);
        });
    }
}
