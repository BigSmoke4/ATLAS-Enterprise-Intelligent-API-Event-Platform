using Atlas.Modules.APIManagement.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.APIManagement.Application;

/// <summary>
/// Sortable fields for the APIManagement list endpoints. Kept next to the
/// service that executes the query so the whitelist and the ordering can never
/// drift apart; the controllers only read <c>Fields</c> to validate input.
/// </summary>
public static class ApiCatalogSorting
{
    /// <summary>GET /api/v1/apis — default ordering: name ascending.</summary>
    public static readonly SortSpec<ApiDefinition> Apis = SortSpec<ApiDefinition>.Create(
        defaultField: "name",
        defaultOrdering: q => q.OrderBy(a => a.Name),
        ("name", (desc, q) => desc ? q.OrderByDescending(a => a.Name) : q.OrderBy(a => a.Name)),
        ("basePath", (desc, q) => desc ? q.OrderByDescending(a => a.BasePath) : q.OrderBy(a => a.BasePath)),
        ("isActive", (desc, q) => desc ? q.OrderByDescending(a => a.IsActive) : q.OrderBy(a => a.IsActive)),
        ("createdAtUtc", (desc, q) => desc ? q.OrderByDescending(a => a.CreatedAtUtc) : q.OrderBy(a => a.CreatedAtUtc)));

    /// <summary>GET /api/v1/apis/routes — default ordering: path then HTTP method.</summary>
    public static readonly SortSpec<ApiRoute> Routes = SortSpec<ApiRoute>.Create(
        defaultField: "path",
        defaultOrdering: q => q.OrderBy(r => r.Path).ThenBy(r => r.HttpMethod),
        ("path", (desc, q) => desc ? q.OrderByDescending(r => r.Path) : q.OrderBy(r => r.Path)),
        ("httpMethod", (desc, q) => desc ? q.OrderByDescending(r => r.HttpMethod) : q.OrderBy(r => r.HttpMethod)),
        ("maxRetries", (desc, q) => desc ? q.OrderByDescending(r => r.MaxRetries) : q.OrderBy(r => r.MaxRetries)),
        ("createdAtUtc", (desc, q) => desc ? q.OrderByDescending(r => r.CreatedAtUtc) : q.OrderBy(r => r.CreatedAtUtc)));
}
