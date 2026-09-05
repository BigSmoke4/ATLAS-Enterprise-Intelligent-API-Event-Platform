using Atlas.Modules.APIManagement.Domain;
using Atlas.Modules.APIManagement.Infrastructure;
using Atlas.Shared.Application;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.APIManagement.Application;

public class ApiCatalogService : IApiCatalogService
{
    private readonly ApiManagementDbContext _db;
    public ApiCatalogService(ApiManagementDbContext db) => _db = db;

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

    public async Task<Result> AddRouteAsync(Guid organizationId, Guid apiVersionId, string path, string httpMethod, CancellationToken ct = default)
    {
        var version = await _db.ApiVersions.Include(v => v.Routes)
            .FirstOrDefaultAsync(v => v.Id == apiVersionId && v.OrganizationId == organizationId, ct);
        if (version is null) return Result.Failure("API version not found.", "NOT_FOUND");

        try
        {
            version.AddRoute(path, httpMethod);
            await _db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message, "VALIDATION_ERROR");
        }
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
}
