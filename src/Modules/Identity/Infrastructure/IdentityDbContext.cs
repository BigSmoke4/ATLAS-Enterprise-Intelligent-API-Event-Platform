using Atlas.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Identity.Infrastructure;

/// <summary>
/// Owns Identity tables (users/roles/claims) plus ApiKeys. No other module may
/// reference this DbContext directly — cross-module reads go through
/// Atlas.Modules.Identity.Application services and DTOs only.
/// </summary>
public class IdentityDbContext : IdentityDbContext<AtlasUser, AtlasRole, Guid>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AtlasUser>(b =>
        {
            b.ToTable("Users", "identity");
            b.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
        });
        builder.Entity<AtlasRole>(b => b.ToTable("Roles", "identity"));
        builder.Entity<IdentityUserRole<Guid>>(b => b.ToTable("UserRoles", "identity"));
        builder.Entity<IdentityUserClaim<Guid>>(b => b.ToTable("UserClaims", "identity"));
        builder.Entity<IdentityUserLogin<Guid>>(b => b.ToTable("UserLogins", "identity"));
        builder.Entity<IdentityRoleClaim<Guid>>(b => b.ToTable("RoleClaims", "identity"));
        builder.Entity<IdentityUserToken<Guid>>(b => b.ToTable("UserTokens", "identity"));

        builder.Entity<ApiKey>(b =>
        {
            b.ToTable("ApiKeys", "identity");
            b.HasKey(k => k.Id);
            b.Property(k => k.HashedKey).HasMaxLength(128).IsRequired();
            b.HasIndex(k => k.HashedKey).IsUnique();
            b.Property(k => k.Prefix).HasMaxLength(16).IsRequired();
            b.Property(k => k.Name).HasMaxLength(128).IsRequired();
            b.Property(k => k.RowVersion).IsRowVersion();
            b.HasIndex(k => k.OrganizationId);
        });
    }
}
