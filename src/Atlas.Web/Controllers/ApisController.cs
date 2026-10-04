using Atlas.Modules.APIManagement.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Razor "APIs" catalog page. Read-only and thin: renders the tenant's
/// registered APIs from IApiCatalogService — the same application service
/// the JSON endpoints use. Tenant scope follows OrganizationScopeResolver.
/// </summary>
[Authorize]
public sealed class ApisController : Controller
{
    private readonly IApiCatalogService _catalog;
    public ApisController(IApiCatalogService catalog) => _catalog = catalog;

    [HttpGet("/Apis")]
    public async Task<IActionResult> Index([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        var apis = organizationId == Guid.Empty
            ? Array.Empty<ApiSummaryDto>()
            : (await _catalog.ListApisAsync(organizationId, 1, 100, ct)).ToArray();
        return View(apis);
    }
}
