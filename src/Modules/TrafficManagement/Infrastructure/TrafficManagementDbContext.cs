using Atlas.Modules.TrafficManagement.Domain;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.TrafficManagement.Infrastructure;

public sealed class TrafficManagementDbContext : DbContext
{
    private readonly ITenantContext _tenant;
    public TrafficManagementDbContext(DbContextOptions<TrafficManagementDbContext> options, ITenantContext tenant) : base(options) => _tenant = tenant;
    public DbSet<TrafficPolicyConfiguration> Policies => Set<TrafficPolicyConfiguration>();
    public DbSet<TrafficPolicyTarget> Targets => Set<TrafficPolicyTarget>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("trafficmanagement");
        builder.Entity<TrafficPolicyConfiguration>(b =>
        {
            b.ToTable("TrafficPolicies"); b.HasKey(p => p.Id);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(p => new { p.OrganizationId, p.ServiceId }).IsUnique();
            b.Property(p => p.Strategy).HasConversion<string>().HasMaxLength(32);
            b.Property(p => p.Mode).HasConversion<string>().HasMaxLength(32);
            b.HasQueryFilter(p => !_tenant.HasOrganization || p.OrganizationId == _tenant.CurrentOrganizationId);
            b.HasMany(p => p.Targets).WithOne().HasForeignKey(t => t.PolicyId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<TrafficPolicyTarget>(b =>
        {
            b.ToTable("TrafficPolicyTargets"); b.HasKey(t => t.Id);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(t => new { t.PolicyId, t.InstanceId }).IsUnique();
            b.HasQueryFilter(t => !_tenant.HasOrganization || t.OrganizationId == _tenant.CurrentOrganizationId);
        });
    }
}
