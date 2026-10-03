using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Modules.DeploymentIntelligence.Infrastructure;

public sealed class DeploymentIntelligenceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<DeploymentIntelligenceDbContext>
{
    public DeploymentIntelligenceDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=atlas;Username=atlas;Password=atlas";
        var options = new DbContextOptionsBuilder<DeploymentIntelligenceDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new DeploymentIntelligenceDbContext(options, new DesignTimeTenantContext());
    }
}
