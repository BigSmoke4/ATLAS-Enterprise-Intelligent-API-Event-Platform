using Atlas.Modules.APIManagement.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.APIManagement.Application;

public sealed class ApiRoutePolicyProvider : IRoutePolicyProvider
{
    private readonly ApiManagementDbContext _db;
    public ApiRoutePolicyProvider(ApiManagementDbContext db) => _db = db;

    public Task<RoutePolicySnapshot?> FindAsync(Guid? organizationId, string path, string method, CancellationToken ct = default)
    {
        if (!organizationId.HasValue || organizationId == Guid.Empty) return Task.FromResult<RoutePolicySnapshot?>(null);
        var normalizedMethod = method.ToUpperInvariant();
        return _db.ApiRoutes.AsNoTracking()
            .Where(route => route.OrganizationId == organizationId.Value && route.Path == path && route.HttpMethod == normalizedMethod && route.RateLimit != null)
            .Select(route => new RoutePolicySnapshot(route.RateLimit!.LimitPerWindow, route.RateLimit.Window, route.RateLimit.Scope.ToString()))
            .FirstOrDefaultAsync(ct);
    }
}
