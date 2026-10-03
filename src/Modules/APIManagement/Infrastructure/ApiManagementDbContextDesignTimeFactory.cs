using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Modules.APIManagement.Infrastructure;

public sealed class ApiManagementDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApiManagementDbContext>
{
    public ApiManagementDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=atlas;Username=atlas;Password=atlas";
        var options = new DbContextOptionsBuilder<ApiManagementDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new ApiManagementDbContext(options, new DesignTimeTenantContext());
    }
}
