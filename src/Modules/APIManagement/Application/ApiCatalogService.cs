using Atlas.Modules.APIManagement.Domain;
using Atlas.Modules.APIManagement.Infrastructure;
using Atlas.Shared.Application;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.APIManagement.Application;

public class ApiCatalogService : IApiCatalogService
{
    private readonly ApiManagementDbContext _db;
    private readonly ICacheService? _cache;

    public ApiCatalogService(ApiManagementDbContext db, ICacheService? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Result<Guid>> RegisterApiAsync(Guid organizationId, string name, string basePath, CancellationToken ct = default)
    {
        var exists = await _db.ApiDefinitions.AnyAsync(a => a.OrganizationId == organizationId && a.BasePath == basePath.TrimEnd('/'), ct);
        if (exists) return Result.Failure<Guid>($"An API with base path '{basePath}' already exists for this organization.", "DUPLICATE_BASE_PATH");

        try
        {
            var api = ApiDefinition.Create(organizationId, name, basePath);
            _db.ApiDefinitions.Add(api);
            await _db.SaveChangesAsync(ct);
            return Result.Success(api.Id);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR");
        }
    }

    public async Task<Result<Guid>> AddVersionAsync(Guid organizationId, Guid apiDefinitionId, int versionNumber, CancellationToken ct = default)
    {
        var api = await _db.ApiDefinitions.Include(a => a.Versions)
            .FirstOrDefaultAsync(a => a.Id == apiDefinitionId && a.OrganizationId == organizationId, ct);
        if (api is null) return Result.Failure<Guid>("API not found.", "NOT_FOUND");

        try
        {
            var version = api.AddVersion(versionNumber);
            await _db.SaveChangesAsync(ct);
            return Result.Success(version.Id);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<Guid>(ex.Message, "DUPLICATE_VERSION");
        }
    }

    public async Task<Result> AddRouteAsync(Guid organizationId, Guid apiVersionId, string path, string httpMethod, Guid? targetServiceId = null, CancellationToken ct = default)
    {
        var version = await _db.ApiVersions.Include(v => v.Routes)
            .FirstOrDefaultAsync(v => v.Id == apiVersionId && v.OrganizationId == organizationId, ct);
        if (version is null) return Result.Failure("API version not found.", "NOT_FOUND");

        try
        {
            var route = version.AddRoute(path, httpMethod);
            route.SetTargetService(targetServiceId);
            await _db.SaveChangesAsync(ct);
            await InvalidateRouteCacheAsync(organizationId, route.Path, route.HttpMethod, ct);
            return Result.Success();
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message, "VALIDATION_ERROR");
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message, "CONFLICT");
        }
    }

    public async Task<Result> ConfigureRouteAsync(Guid organizationId, Guid routeId, RateLimitPolicy? rateLimit, TimeSpan timeout, int maxRetries, CancellationToken ct = default)
    {
        var route = await _db.ApiRoutes.FirstOrDefaultAsync(r => r.Id == routeId && r.OrganizationId == organizationId, ct);
        if (route is null) return Result.Failure("Route not found.", "NOT_FOUND");
        try
        {
            route.SetRateLimit(rateLimit);
            route.SetTimeout(timeout);
            route.SetRetryPolicy(maxRetries);
            await _db.SaveChangesAsync(ct);
            // Explicit invalidation: a policy change must be visible on the
            // next request, not after the cache TTL expires.
            await InvalidateRouteCacheAsync(organizationId, route.Path, route.HttpMethod, ct);
            return Result.Success();
        }
        catch (ArgumentException ex) { return Result.Failure(ex.Message, "VALIDATION_ERROR"); }
    }

    public async Task<Result> SetRouteTargetServiceAsync(Guid organizationId, Guid routeId, Guid? serviceId, CancellationToken ct = default)
    {
        var route = await _db.ApiRoutes.FirstOrDefaultAsync(r => r.Id == routeId && r.OrganizationId == organizationId, ct);
        if (route is null) return Result.Failure("Route not found.", "NOT_FOUND");

        route.SetTargetService(serviceId);
        await _db.SaveChangesAsync(ct);
        await InvalidateRouteCacheAsync(organizationId, route.Path, route.HttpMethod, ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ApiRouteDto>> ListRoutesAsync(Guid organizationId, Guid apiVersionId, CancellationToken ct = default)
        => await _db.ApiRoutes.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.ApiVersionId == apiVersionId)
            .OrderBy(r => r.Path).ThenBy(r => r.HttpMethod)
            .Select(r => new ApiRouteDto(r.Id, r.Path, r.HttpMethod, r.RateLimit, r.Timeout, r.MaxRetries, r.TargetServiceId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ApiRouteDto>> ListAllRoutesAsync(Guid organizationId, int page = 1, int pageSize = 200, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        return await _db.ApiRoutes.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId)
            .OrderBy(r => r.Path).ThenBy(r => r.HttpMethod)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new ApiRouteDto(r.Id, r.Path, r.HttpMethod, r.RateLimit, r.Timeout, r.MaxRetries, r.TargetServiceId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApiSummaryDto>> ListApisAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        return await _db.ApiDefinitions
            .Where(a => a.OrganizationId == organizationId)
            .OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new ApiSummaryDto(a.Id, a.Name, a.BasePath, a.IsActive, a.Versions.Count))
            .AsNoTracking()
            .ToListAsync(ct);
    }

    private async Task InvalidateRouteCacheAsync(Guid organizationId, string path, string method, CancellationToken ct)
    {
        if (_cache is null) return;
        await _cache.RemoveAsync(ApiRoutePolicyProvider.CacheKey(organizationId, method, path), ct);
    }
}
