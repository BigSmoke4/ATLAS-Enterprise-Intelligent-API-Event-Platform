using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Modules.TrafficManagement.Infrastructure;

public sealed class TrafficManagementDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TrafficManagementDbContext>
{
    public TrafficManagementDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=atlas;Username=atlas;Password=atlas";
        var options = new DbContextOptionsBuilder<TrafficManagementDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new TrafficManagementDbContext(options, new DesignTimeTenantContext());
    }
}
