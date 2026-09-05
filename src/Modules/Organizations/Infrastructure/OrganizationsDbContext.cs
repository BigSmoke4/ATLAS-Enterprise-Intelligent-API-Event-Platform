using Atlas.Modules.Organizations.Domain;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Organizations.Infrastructure;

public class OrganizationsDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public OrganizationsDbContext(DbContextOptions<OrganizationsDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Domain.Environment> Environments => Set<Domain.Environment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("organizations");

        builder.Entity<Organization>(b =>
        {
            b.ToTable("Organizations");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(256).IsRequired();
            b.Property(o => o.Slug).HasMaxLength(128).IsRequired();
            b.HasIndex(o => o.Slug).IsUnique();
            b.Property(o => o.RowVersion).IsRowVersion();
        });

        builder.Entity<Team>(b =>
        {
            b.ToTable("Teams");
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).HasMaxLength(256).IsRequired();
            b.HasIndex(t => t.OrganizationId);
            // Tenant isolation enforced at the query layer, not just in application code:
            b.HasQueryFilter(t => !_tenantContext.HasOrganization || t.OrganizationId == _tenantContext.CurrentOrganizationId);
        });

        builder.Entity<Domain.Environment>(b =>
        {
            b.ToTable("Environments");
            b.HasKey(e => e.Id);
            b.Property(e => e.Name).HasMaxLength(128).IsRequired();
            b.HasIndex(e => e.OrganizationId);
            b.HasQueryFilter(e => !_tenantContext.HasOrganization || e.OrganizationId == _tenantContext.CurrentOrganizationId);
        });
    }
}
