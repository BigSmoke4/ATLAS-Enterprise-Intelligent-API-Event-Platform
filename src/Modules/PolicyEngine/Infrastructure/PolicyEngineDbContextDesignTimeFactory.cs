using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Modules.PolicyEngine.Infrastructure;

public sealed class PolicyEngineDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PolicyEngineDbContext>
{
    public PolicyEngineDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=atlas;Username=atlas;Password=atlas";
        var options = new DbContextOptionsBuilder<PolicyEngineDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new PolicyEngineDbContext(options, new DesignTimeTenantContext());
    }
}
