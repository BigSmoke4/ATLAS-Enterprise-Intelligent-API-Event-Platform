using Atlas.Modules.Organizations.Domain;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.Organizations.Infrastructure;

public sealed class DevelopmentSeedService : BackgroundService
{
    public static readonly Guid DemoOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DevelopmentSeedService> _logger;
    public DevelopmentSeedService(IServiceScopeFactory scopeFactory, ILogger<DevelopmentSeedService> logger) { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OrganizationsDbContext>();
            if (!await db.Organizations.AnyAsync(o => o.Slug == "atlas-demo", stoppingToken))
            {
                db.Organizations.Add(Organization.Create("ATLAS Development", "atlas-demo", DemoOrganizationId));
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Seeded deterministic ATLAS development organization {OrganizationId}.", DemoOrganizationId);
            }
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        { _logger.LogError(ex, "Development seed failed. Apply module migrations before enabling Seed:Development."); }
    }
}
