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

        services.AddAuthorization(options =>
        {
            foreach (var role in AtlasRoles.All)
            {
                options.AddPolicy($"Role:{role}", policy => policy.RequireRole(role));
            }

            options.AddPolicy("SameOrganization", policy =>
                policy.Requirements.Add(new OrganizationAccessRequirement()));
        });
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Auth endpoints (login/logout/register/api-key management) are planned
        // for Atlas.Web/Controllers/Identity — not yet built (see README).
    }
}
