using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Modules.IncidentManagement.Infrastructure;

public sealed class IncidentManagementDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IncidentManagementDbContext>
{
    public IncidentManagementDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=atlas;Username=atlas;Password=atlas";
        var options = new DbContextOptionsBuilder<IncidentManagementDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new IncidentManagementDbContext(options, new DesignTimeTenantContext());
    }
}
