using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Modules.Observability.Infrastructure;

public sealed class ObservabilityDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ObservabilityDbContext>
{
    public ObservabilityDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=atlas;Username=atlas;Password=atlas";
        var options = new DbContextOptionsBuilder<ObservabilityDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new ObservabilityDbContext(options, new DesignTimeTenantContext());
    }
}
