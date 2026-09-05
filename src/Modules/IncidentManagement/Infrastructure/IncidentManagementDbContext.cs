using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.IncidentManagement.Infrastructure;

public class IncidentManagementDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public IncidentManagementDbContext(DbContextOptions<IncidentManagementDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentTimelineEntry> TimelineEntries => Set<IncidentTimelineEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("incidentmanagement");

        builder.Entity<Incident>(b =>
        {
            b.ToTable("Incidents");
            b.HasKey(i => i.Id);
            b.Property(i => i.Title).HasMaxLength(512).IsRequired();
            b.Property(i => i.RootCause).HasMaxLength(4000);
            b.Property(i => i.Mitigation).HasMaxLength(4000);
            b.HasIndex(i => new { i.OrganizationId, i.Status });
            b.HasQueryFilter(i => !_tenantContext.HasOrganization || i.OrganizationId == _tenantContext.CurrentOrganizationId);
            b.HasMany(i => i.Timeline).WithOne().HasForeignKey(t => t.IncidentId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IncidentTimelineEntry>(b =>
        {
            b.ToTable("IncidentTimelineEntries");
            b.HasKey(t => t.Id);
            b.Property(t => t.Note).HasMaxLength(2000).IsRequired();
        });
    }
}
