using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Modules.Organizations.Infrastructure;

public sealed class OrganizationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrganizationsDbContext>
{
    public OrganizationsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=atlas;Username=atlas;Password=atlas";
        var options = new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new OrganizationsDbContext(options, new DesignTimeTenantContext());
    }
}
