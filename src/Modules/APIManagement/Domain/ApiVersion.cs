using Atlas.Shared.Domain;

namespace Atlas.Modules.APIManagement.Domain;

public enum ApiVersionStatus { Draft, Active, Deprecated, Retired }

public class ApiVersion : TenantEntity
{
    public Guid ApiDefinitionId { get; private set; }
    public int VersionNumber { get; private set; }
    public ApiVersionStatus Status { get; private set; } = ApiVersionStatus.Draft;

    private readonly List<ApiRoute> _routes = new();
    public IReadOnlyCollection<ApiRoute> Routes => _routes.AsReadOnly();

    private ApiVersion() { }

    public static ApiVersion Create(Guid organizationId, Guid apiDefinitionId, int versionNumber)
    {
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        return new ApiVersion { OrganizationId = organizationId, ApiDefinitionId = apiDefinitionId, VersionNumber = versionNumber };
    }

    public ApiRoute AddRoute(string path, string httpMethod, RateLimitPolicy? rateLimit = null)
    {
        var route = ApiRoute.Create(OrganizationId, Id, path, httpMethod, rateLimit);
        _routes.Add(route);
        return route;
    }

    public void Activate() => Status = ApiVersionStatus.Active;
    public void Deprecate() => Status = ApiVersionStatus.Deprecated;
    public void Retire() => Status = ApiVersionStatus.Retired;
}
