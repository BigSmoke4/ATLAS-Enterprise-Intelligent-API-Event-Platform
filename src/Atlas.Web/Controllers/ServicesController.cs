using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.ServiceRegistry.Application;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>Serves both the Razor "Services" page and its backing JSON API — both call the same real IServiceHealthService.</summary>
[Authorize]
public class ServicesController : Controller
{
    private readonly IServiceHealthService _serviceHealth;
    public ServicesController(IServiceHealthService serviceHealth) => _serviceHealth = serviceHealth;

    public record RegisterServiceRequest(Guid OrganizationId, Guid EnvironmentId, string Name);
    public record RegisterInstanceRequest(Guid OrganizationId, string HostAndPort);
    public record RecordHealthCheckRequest(Guid OrganizationId, bool Success);
    public record AddDependencyRequest(Guid OrganizationId, Guid DependsOnServiceId);

    /// <summary>
    /// Razor view. Real data only. The organization is resolved from the
    /// caller's org_id claim unless an explicit ?organizationId is given —
    /// and an explicit value must match the claim (PlatformAdmin may view
    /// any). Anything else is 403: the view path enforces the same tenant
    /// isolation as the JSON endpoints.
    /// </summary>
    [HttpGet("/Services")]
    public async Task<IActionResult> Index([FromQuery] Guid organizationId, CancellationToken ct)
    {
        var denial = ResolveOrganizationScope(ref organizationId);
        if (denial is not null) return denial;

        var statuses = organizationId == Guid.Empty
            ? Array.Empty<ServiceStatusDto>()
            : (await _serviceHealth.GetStatusAsync(organizationId, 1, 50, ct)).ToArray();
        return View(statuses);
    }

    [HttpGet("/api/v1/services")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<ServiceStatusDto>>> ListJson([FromQuery] Guid organizationId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _serviceHealth.GetStatusAsync(organizationId, page, pageSize, ct));

    [HttpPost("/api/v1/services")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Register([FromBody] RegisterServiceRequest request, CancellationToken ct)
    {
        var result = await _serviceHealth.RegisterServiceAsync(request.OrganizationId, request.EnvironmentId, request.Name, ct);
        if (!result.IsSuccess) return Conflict(new ProblemDetails { Title = result.Error });
        return Ok(new { serviceId = result.Value });
    }

    [HttpPost("/api/v1/services/{serviceId:guid}/instances")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> RegisterInstance(Guid serviceId, [FromBody] RegisterInstanceRequest request, CancellationToken ct)
    {
        var result = await _serviceHealth.RegisterInstanceAsync(request.OrganizationId, serviceId, request.HostAndPort, ct);
        if (!result.IsSuccess) return result.ErrorCode switch
        {
            "NOT_FOUND" => NotFound(new ProblemDetails { Title = result.Error }),
            "CONFLICT" => Conflict(new ProblemDetails { Title = result.Error }),
            _ => BadRequest(new ProblemDetails { Title = result.Error })
        };
        return Ok(new { instanceId = result.Value });
    }

    [HttpGet("/api/v1/services/{serviceId:guid}/dependencies")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<ServiceDependencyDto>>> Dependencies(Guid serviceId, [FromQuery] Guid organizationId, CancellationToken ct)
        => Ok(await _serviceHealth.GetDependenciesAsync(organizationId, serviceId, ct));

    [HttpPost("/api/v1/services/{serviceId:guid}/dependencies")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> AddDependency(Guid serviceId, [FromBody] AddDependencyRequest request, CancellationToken ct)
    {
        if (!User.IsInRole("PlatformAdmin") && User.FindFirst("org_id")?.Value != request.OrganizationId.ToString()) return Forbid();
        var result = await _serviceHealth.AddDependencyAsync(request.OrganizationId, serviceId, request.DependsOnServiceId, ct);
        if (!result.IsSuccess) return result.ErrorCode switch { "NOT_FOUND" => NotFound(new ProblemDetails { Title = result.Error }), "CONFLICT" => Conflict(new ProblemDetails { Title = result.Error }), _ => BadRequest(new ProblemDetails { Title = result.Error }) };
        return NoContent();
    }

    [HttpPost("/api/v1/service-instances/{instanceId:guid}/health-check")]
    public async Task<IActionResult> RecordHealthCheck(Guid instanceId, [FromBody] RecordHealthCheckRequest request, CancellationToken ct)
    {
        var result = await _serviceHealth.RecordHealthCheckAsync(request.OrganizationId, instanceId, request.Success, ct);
        if (!result.IsSuccess) return NotFound(new ProblemDetails { Title = result.Error });
        return NoContent();
    }

    /// <summary>Tenant scope for the MVC page path — see OrganizationScopeResolver for the exact rules.</summary>
    private IActionResult? ResolveOrganizationScope(ref Guid organizationId)
        => OrganizationScopeResolver.TryResolve(User, ref organizationId) ? null : Forbid();
}
