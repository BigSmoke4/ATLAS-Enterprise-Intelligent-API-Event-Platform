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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("deploymentintelligence");

        builder.Entity<Deployment>(b =>
        {
            b.ToTable("Deployments");
            b.HasKey(d => d.Id);
            b.Property(d => d.Version).HasMaxLength(128).IsRequired();
            b.Property(d => d.Environment).HasMaxLength(64).IsRequired();
            b.Property(d => d.CommitSha).HasMaxLength(64).IsRequired();
            b.Property(d => d.Author).HasMaxLength(256);
            b.HasIndex(d => new { d.OrganizationId, d.ServiceId, d.DeployedAtUtc });
            b.HasQueryFilter(d => !_tenantContext.HasOrganization || d.OrganizationId == _tenantContext.CurrentOrganizationId);
        });
    }
}
