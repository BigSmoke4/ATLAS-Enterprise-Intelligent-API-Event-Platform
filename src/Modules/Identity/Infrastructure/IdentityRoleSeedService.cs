using Atlas.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.Identity.Infrastructure;

/// <summary>
/// Ensures the fixed RBAC role set exists. Roles are reference data for the
/// whole platform — registration assigns Viewer on signup, so a missing
/// role row makes an otherwise-valid first registration fail. Seeding is
/// idempotent and runs on every startup.
///
/// An optional deterministic development operator account can be created
/// when BOTH Seed:AdminEmail and Seed:AdminPassword are configured (never
/// in Production config sources). The password is read from configuration/
/// user-secrets/environment variables only — plain-text defaults are never
/// shipped in the repository.
/// </summary>
public sealed class IdentityRoleSeedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentityRoleSeedService> _logger;

    public IdentityRoleSeedService(IServiceScopeFactory scopeFactory, IConfiguration configuration,
        ILogger<IdentityRoleSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AtlasRole>>();
            foreach (var role in AtlasRoles.All)
            {
                if (!await roles.RoleExistsAsync(role))
                {
                    var result = await roles.CreateAsync(new AtlasRole { Name = role });
                    if (!result.Succeeded)
                        _logger.LogWarning("Role seeding for {Role} reported: {Errors}", role, string.Join("; ", result.Errors.Select(e => e.Description)));
                }
            }

            var adminEmail = _configuration["Seed:AdminEmail"];
            var adminPassword = _configuration["Seed:AdminPassword"];
            if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<AtlasUser>>();
                if (await users.FindByEmailAsync(adminEmail) is null)
                {
                    var organizationId = Guid.TryParse(_configuration["Seed:AdminOrganizationId"], out var parsed) ? parsed : (Guid?)null;
                    var admin = new AtlasUser
                    {
                        UserName = adminEmail,
                        Email = adminEmail,
                        DisplayName = "ATLAS Seed Administrator",
                        EmailConfirmed = true,
                        CurrentOrganizationId = organizationId
                    };
                    var created = await users.CreateAsync(admin, adminPassword);
                    if (created.Succeeded)
                    {
                        await users.AddToRoleAsync(admin, AtlasRoles.PlatformAdmin);
                        _logger.LogInformation("Seeded development administrator {Email}.", adminEmail);
                    }
                    else
                    {
                        _logger.LogWarning("Administrator seeding reported: {Errors}", string.Join("; ", created.Errors.Select(e => e.Description)));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Seeding must never prevent the process from starting; without a
            // migrated database this will fail, and the error tells the
            // operator exactly what to do (run scripts/migrate.sh).
            _logger.LogError(ex, "Identity role seeding failed. Apply identity migrations before first use.");
        }
    }
}
