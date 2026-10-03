using Atlas.Shared.Application;
using Atlas.Modules.APIManagement.Domain;

namespace Atlas.Modules.APIManagement.Application;

/// <summary>Application-layer use cases for registering and querying APIs. Controllers call only this — never the DbContext.</summary>
public interface IApiCatalogService
{
    Task<Result<Guid>> RegisterApiAsync(Guid organizationId, string name, string basePath, CancellationToken ct = default);
    Task<Result<Guid>> AddVersionAsync(Guid organizationId, Guid apiDefinitionId, int versionNumber, CancellationToken ct = default);
    Task<Result> AddRouteAsync(Guid organizationId, Guid apiVersionId, string path, string httpMethod, CancellationToken ct = default);
    Task<Result> ConfigureRouteAsync(Guid organizationId, Guid routeId, RateLimitPolicy? rateLimit, TimeSpan timeout, int maxRetries, CancellationToken ct = default);
    Task<IReadOnlyList<ApiRouteDto>> ListRoutesAsync(Guid organizationId, Guid apiVersionId, CancellationToken ct = default);
    Task<IReadOnlyList<ApiSummaryDto>> ListApisAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default);
}

public record ApiSummaryDto(Guid Id, string Name, string BasePath, bool IsActive, int VersionCount);
public record ApiRouteDto(Guid Id, string Path, string HttpMethod, RateLimitPolicy? RateLimit, TimeSpan Timeout, int MaxRetries);
