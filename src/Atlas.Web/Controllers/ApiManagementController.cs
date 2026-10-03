using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.APIManagement.Application;
using Atlas.Modules.APIManagement.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Thin controller: every action delegates to IApiCatalogService and
/// translates its Result/Result&lt;T&gt; into an HTTP status + ProblemDetails.
/// No business logic, no DbContext usage here — per the master prompt's
/// "controllers must remain thin" rule.
/// </summary>
[ApiController]
[Route("api/v1/apis")]
[Authorize]
public class ApiManagementController : ControllerBase
{
    private readonly IApiCatalogService _catalog;
    public ApiManagementController(IApiCatalogService catalog) => _catalog = catalog;

    public record RegisterApiRequest(Guid OrganizationId, string Name, string BasePath);
    public record AddVersionRequest(Guid OrganizationId, int VersionNumber);
    public record AddRouteRequest(Guid OrganizationId, string Path, string HttpMethod);
    public record ConfigureRouteRequest(Guid OrganizationId, RateLimitPolicy? RateLimit, TimeSpan Timeout, int MaxRetries);

    [HttpGet]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<ApiSummaryDto>>> List([FromQuery] Guid organizationId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _catalog.ListApisAsync(organizationId, page, pageSize, ct));

    [HttpPost]
    [Authorize(Policy = "Role:OrganizationAdmin")]
    public async Task<IActionResult> Register([FromBody] RegisterApiRequest request, CancellationToken ct)
    {
        var result = await _catalog.RegisterApiAsync(request.OrganizationId, request.Name, request.BasePath, ct);
        if (!result.IsSuccess) return ToProblem(result.Error!, result.ErrorCode!);
        return CreatedAtAction(nameof(List), new { organizationId = request.OrganizationId }, new { id = result.Value });
    }

    [HttpPost("{apiId:guid}/versions")]
    [Authorize(Policy = "Role:OrganizationAdmin")]
    public async Task<IActionResult> AddVersion(Guid apiId, [FromBody] AddVersionRequest request, CancellationToken ct)
    {
        var result = await _catalog.AddVersionAsync(request.OrganizationId, apiId, request.VersionNumber, ct);
        if (!result.IsSuccess) return ToProblem(result.Error!, result.ErrorCode!);
        return Ok(new { versionId = result.Value });
    }

    [HttpGet("versions/{apiVersionId:guid}/routes")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<ApiRouteDto>>> ListRoutes(Guid apiVersionId, [FromQuery] Guid organizationId, CancellationToken ct)
        => Ok(await _catalog.ListRoutesAsync(organizationId, apiVersionId, ct));

    [HttpPost("routes/{routeId:guid}/configure")]
    [Authorize(Policy = "Role:OrganizationAdmin")]
    public async Task<IActionResult> ConfigureRoute(Guid routeId, ConfigureRouteRequest request, CancellationToken ct)
    {
        var result = await _catalog.ConfigureRouteAsync(request.OrganizationId, routeId, request.RateLimit, request.Timeout, request.MaxRetries, ct);
        if (!result.IsSuccess) return result.ErrorCode == "NOT_FOUND" ? NotFound(new ProblemDetails { Title = result.Error }) : BadRequest(new ProblemDetails { Title = result.Error });
        return NoContent();
    }

    [HttpPost("versions/{apiVersionId:guid}/routes")]
    [Authorize(Policy = "Role:OrganizationAdmin")]
    public async Task<IActionResult> AddRoute(Guid apiVersionId, [FromBody] AddRouteRequest request, CancellationToken ct)
    {
        var result = await _catalog.AddRouteAsync(request.OrganizationId, apiVersionId, request.Path, request.HttpMethod, ct);
        if (!result.IsSuccess) return ToProblem(result.Error!, result.ErrorCode!);
        return NoContent();
    }

    private IActionResult ToProblem(string error, string code) => code switch
    {
        "NOT_FOUND" => NotFound(new ProblemDetails { Title = error, Status = StatusCodes.Status404NotFound }),
        "VALIDATION_ERROR" => BadRequest(new ProblemDetails { Title = error, Status = StatusCodes.Status400BadRequest }),
        _ => Conflict(new ProblemDetails { Title = error, Status = StatusCodes.Status409Conflict })
    };
}
