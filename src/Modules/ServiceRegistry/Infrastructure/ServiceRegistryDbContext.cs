using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.ServiceRegistry.Infrastructure;

public class ServiceRegistryDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public ServiceRegistryDbContext(DbContextOptions<ServiceRegistryDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<RegisteredService> Services => Set<RegisteredService>();
    public DbSet<ServiceInstance> Instances => Set<ServiceInstance>();
    public DbSet<ServiceDependency> Dependencies => Set<ServiceDependency>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("serviceregistry");

        builder.Entity<RegisteredService>(b =>
        {
            b.ToTable("Services");
            b.HasKey(s => s.Id);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.Property(s => s.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(s => new { s.OrganizationId, s.EnvironmentId, s.Name }).IsUnique();
            b.HasQueryFilter(s => !_tenantContext.HasOrganization || s.OrganizationId == _tenantContext.CurrentOrganizationId);
            b.HasMany(s => s.Instances).WithOne().HasForeignKey(i => i.ServiceId).OnDelete(DeleteBehavior.Cascade);
            // Dependencies are modelled as their own entity (ServiceDependency
            // below) with a composite key, not as a primitive collection on the
            // service: an edge is a first-class row that can carry attributes
            // later, and dependency lookups ("what depends on this?") are an
            // indexed query instead of a JSON scan.
        });

        builder.Entity<ServiceDependency>(b =>
        {
            b.ToTable("ServiceDependencies");
            b.HasKey(d => new { d.OrganizationId, d.ServiceId, d.DependsOnServiceId });
            b.Ignore(x => x.RowVersion);
            b.HasIndex(d => new { d.OrganizationId, d.DependsOnServiceId });
            b.HasQueryFilter(d => !_tenantContext.HasOrganization || d.OrganizationId == _tenantContext.CurrentOrganizationId);
        });

        builder.Entity<ServiceInstance>(b =>
        {
            b.ToTable("ServiceInstances");
            b.HasKey(i => i.Id);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.Property(i => i.HostAndPort).HasMaxLength(256).IsRequired();
            b.HasIndex(i => i.ServiceId);
            b.HasQueryFilter(i => !_tenantContext.HasOrganization || i.OrganizationId == _tenantContext.CurrentOrganizationId);
        });
    }
}
