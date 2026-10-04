using Atlas.Modules.APIManagement.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.APIManagement.Application;

/// <summary>Application-layer use cases for registering and querying APIs. Controllers call only this — never the DbContext.</summary>
public interface IApiCatalogService
{
    Task<Result<Guid>> RegisterApiAsync(Guid organizationId, string name, string basePath, CancellationToken ct = default);
    Task<Result<Guid>> AddVersionAsync(Guid organizationId, Guid apiDefinitionId, int versionNumber, CancellationToken ct = default);
    Task<Result> AddRouteAsync(Guid organizationId, Guid apiVersionId, string path, string httpMethod, Guid? targetServiceId = null, CancellationToken ct = default);
    Task<Result> ConfigureRouteAsync(Guid organizationId, Guid routeId, RateLimitPolicy? rateLimit, TimeSpan timeout, int maxRetries, CancellationToken ct = default);

    /// <summary>Points a route at the registered service that serves it (used for telemetry attribution and traffic routing).</summary>
    Task<Result> SetRouteTargetServiceAsync(Guid organizationId, Guid routeId, Guid? serviceId, CancellationToken ct = default);

    Task<IReadOnlyList<ApiRouteDto>> ListRoutesAsync(Guid organizationId, Guid apiVersionId, CancellationToken ct = default);
    Task<IReadOnlyList<ApiSummaryDto>> ListApisAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default);

    /// <summary>Versions of one API definition (newest first) — the version picker on the API management page.</summary>
    Task<IReadOnlyList<ApiVersionDto>> ListVersionsAsync(Guid organizationId, Guid apiDefinitionId, CancellationToken ct = default);

    /// <summary>Route inventory for the organization across every API version (read model for the API management page).</summary>
    Task<IReadOnlyList<ApiRouteDto>> ListAllRoutesAsync(Guid organizationId, int page = 1, int pageSize = 200, CancellationToken ct = default);
}

public record ApiSummaryDto(Guid Id, string Name, string BasePath, bool IsActive, int VersionCount);
public record ApiVersionDto(Guid Id, int VersionNumber, string Status, int RouteCount);
public record ApiRouteDto(Guid Id, string Path, string HttpMethod, RateLimitPolicy? RateLimit, TimeSpan Timeout, int MaxRetries, Guid? TargetServiceId = null)
{
    /// <summary>Timeout in whole seconds — avoids forcing the browser to parse a TimeSpan.</summary>
    public int TimeoutSeconds => (int)Math.Round(Timeout.TotalSeconds);
}
