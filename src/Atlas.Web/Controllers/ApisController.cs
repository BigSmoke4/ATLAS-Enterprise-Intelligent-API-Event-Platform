using Atlas.Modules.APIManagement.Application;
using Atlas.Modules.Identity.Application;
using Atlas.Modules.Identity.Domain;
using Atlas.Modules.ServiceRegistry.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Razor "APIs" catalog page. Read-only and thin: it renders the tenant's
/// registered APIs from IApiCatalogService, the services a route can target
/// (ServiceRegistry) and the organization's key metadata (Identity) — the
/// same application services the JSON endpoints use. Tenant scope follows
/// OrganizationScopeResolver; key metadata is only loaded for roles that may
/// act on it, so the page never displays what the caller cannot manage.
/// </summary>
[Authorize]
public sealed class ApisController : Controller
{
    private readonly IApiCatalogService _catalog;
    private readonly IServiceHealthService _services;
    private readonly IApiKeyService _apiKeys;

    public ApisController(IApiCatalogService catalog, IServiceHealthService services, IApiKeyService apiKeys)
    {
        _catalog = catalog;
        _services = services;
        _apiKeys = apiKeys;
    }

    [HttpGet("/Apis")]
    public async Task<IActionResult> Index([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        ViewData["OrganizationId"] = organizationId;

        if (organizationId == Guid.Empty)
        {
            return View(Array.Empty<ApiSummaryDto>());
        }

        var apis = await _catalog.ListApisAsync(organizationId, 1, 100, ct);
        ViewData["Services"] = await _services.GetStatusAsync(organizationId, 1, 200, ct);

        if (User.IsInRole(AtlasRoles.OrganizationAdmin) || User.IsInRole(AtlasRoles.PlatformAdmin))
        {
            ViewData["ApiKeys"] = await _apiKeys.ListAsync(organizationId, ct);
        }

        return View(apis);
    }
}
