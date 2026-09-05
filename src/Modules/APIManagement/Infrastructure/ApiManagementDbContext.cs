using Atlas.Modules.APIManagement.Domain;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atlas.Modules.APIManagement.Infrastructure;

public class ApiManagementDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public ApiManagementDbContext(DbContextOptions<ApiManagementDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<ApiDefinition> ApiDefinitions => Set<ApiDefinition>();
    public DbSet<ApiVersion> ApiVersions => Set<ApiVersion>();
    public DbSet<ApiRoute> ApiRoutes => Set<ApiRoute>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("apimanagement");

        var rateLimitConverter = new ValueConverter<RateLimitPolicy?, string?>(
            v => v == null ? null : $"{v.LimitPerWindow}|{v.Window.Ticks}|{v.Scope}",
            v => v == null ? null : ParseRateLimit(v));

        builder.Entity<ApiDefinition>(b =>
        {
            b.ToTable("ApiDefinitions");
            b.HasKey(a => a.Id);
            b.Property(a => a.Name).HasMaxLength(256).IsRequired();
            b.Property(a => a.BasePath).HasMaxLength(256).IsRequired();
            b.HasIndex(a => new { a.OrganizationId, a.BasePath }).IsUnique();
            b.HasQueryFilter(a => !_tenantContext.HasOrganization || a.OrganizationId == _tenantContext.CurrentOrganizationId);
            b.HasMany(a => a.Versions).WithOne().HasForeignKey(v => v.ApiDefinitionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApiVersion>(b =>
        {
            b.ToTable("ApiVersions");
            b.HasKey(v => v.Id);
            b.HasIndex(v => new { v.ApiDefinitionId, v.VersionNumber }).IsUnique();
            b.HasQueryFilter(v => !_tenantContext.HasOrganization || v.OrganizationId == _tenantContext.CurrentOrganizationId);
            b.HasMany(v => v.Routes).WithOne().HasForeignKey(r => r.ApiVersionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApiRoute>(b =>
        {
            b.ToTable("ApiRoutes");
            b.HasKey(r => r.Id);
            b.Property(r => r.Path).HasMaxLength(512).IsRequired();
            b.Property(r => r.HttpMethod).HasMaxLength(10).IsRequired();
            b.HasIndex(r => new { r.ApiVersionId, r.Path, r.HttpMethod }).IsUnique();
            b.Property(r => r.RateLimit).HasConversion(rateLimitConverter).HasMaxLength(128);
            b.HasQueryFilter(r => !_tenantContext.HasOrganization || r.OrganizationId == _tenantContext.CurrentOrganizationId);
        });
    }

    private static RateLimitPolicy ParseRateLimit(string raw)
    {
        var parts = raw.Split('|');
        return new RateLimitPolicy(int.Parse(parts[0]), TimeSpan.FromTicks(long.Parse(parts[1])), Enum.Parse<RateLimitScope>(parts[2]));
    }
}
