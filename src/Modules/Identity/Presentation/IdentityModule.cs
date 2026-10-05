using Atlas.Modules.Identity.Application;
using Atlas.Modules.Identity.Domain;
using Atlas.Modules.Identity.Infrastructure;
using Atlas.Shared.Contracts;
using Atlas.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.Identity.Presentation;

public class IdentityModule : IAtlasModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<IdentityDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));

        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IUserClaimsPrincipalFactory<AtlasUser>, AtlasClaimsPrincipalFactory>();

        services.AddIdentityCore<AtlasUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<AtlasRole>()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Resource-level tenant check (see OrganizationAccessHandler) on top
        // of role policies — registered here since this is where all
        // authorization policy wiring lives.
        services.AddSingleton<IAuthorizationHandler, OrganizationAccessHandler>();

        // Roles and the optional configured development operator are
        // reference data; the seeder is idempotent and fails soft (logs)
        // when the database has not been migrated yet.
        services.AddHostedService<IdentityRoleSeedService>();

        services.AddAuthorization(options =>
        {
            foreach (var role in AtlasRoles.All)
            {
                options.AddPolicy($"Role:{role}", policy => policy.RequireRole(role));
            }

            // Composed read policies. PlatformAdmin is the platform-wide
            // operator and deliberately inherits security-review surface
            // area that individual roles hold.
            options.AddPolicy("AuditRead", policy =>
                policy.RequireRole(AtlasRoles.SecurityEngineer, AtlasRoles.PlatformAdmin));

            options.AddPolicy("SameOrganization", policy =>
                policy.Requirements.Add(new OrganizationAccessRequirement()));
        });
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // The account surface is mapped by Atlas.Web/Controllers/IdentityController
        // (register, login, logout, revoke-all sessions, deactivate/reactivate,
        // API-key create/list/revoke). It lives in the host project because it
        // is HTTP presentation, not module state.
    }
}
