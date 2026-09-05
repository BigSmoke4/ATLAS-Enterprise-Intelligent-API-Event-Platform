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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("serviceregistry");

        builder.Entity<RegisteredService>(b =>
        {
            b.ToTable("Services");
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(s => new { s.OrganizationId, s.EnvironmentId, s.Name }).IsUnique();
            b.HasQueryFilter(s => !_tenantContext.HasOrganization || s.OrganizationId == _tenantContext.CurrentOrganizationId);
            b.HasMany(s => s.Instances).WithOne().HasForeignKey(i => i.ServiceId).OnDelete(DeleteBehavior.Cascade);
            // TODO: DependsOnServiceIds is not yet persisted. EF Core 9 supports
            // primitive collections (List<Guid> as a JSON column), but mapping a
            // read-only IReadOnlyCollection<Guid> backed by a private field needs
            // verifying against a real build before wiring it up — left as a
            // documented gap rather than a guessed-at, unverified configuration.
        });

        builder.Entity<ServiceInstance>(b =>
        {
            b.ToTable("ServiceInstances");
            b.HasKey(i => i.Id);
            b.Property(i => i.HostAndPort).HasMaxLength(256).IsRequired();
            b.HasIndex(i => i.ServiceId);
            b.HasQueryFilter(i => !_tenantContext.HasOrganization || i.OrganizationId == _tenantContext.CurrentOrganizationId);
        });
    }
}
