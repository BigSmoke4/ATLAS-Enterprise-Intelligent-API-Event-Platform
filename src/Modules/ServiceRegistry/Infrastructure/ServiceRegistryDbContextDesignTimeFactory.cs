using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Modules.ServiceRegistry.Infrastructure;

public sealed class ServiceRegistryDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ServiceRegistryDbContext>
{
    public ServiceRegistryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=atlas;Username=atlas;Password=atlas";
        var options = new DbContextOptionsBuilder<ServiceRegistryDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new ServiceRegistryDbContext(options, new DesignTimeTenantContext());
    }
}
