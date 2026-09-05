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

    /// <summary>Razor view. Real data — if organizationId is empty/unset the view shows "No telemetry available." rather than fabricating rows.</summary>
    [HttpGet("/Services")]
    public async Task<IActionResult> Index([FromQuery] Guid organizationId, CancellationToken ct)
    {
        var statuses = organizationId == Guid.Empty
            ? Array.Empty<ServiceStatusDto>()
            : (await _serviceHealth.GetStatusAsync(organizationId, ct)).ToArray();
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
        if (!result.IsSuccess) return NotFound(new ProblemDetails { Title = result.Error });
        return Ok(new { instanceId = result.Value });
    }

    [HttpPost("/api/v1/service-instances/{instanceId:guid}/health-check")]
    public async Task<IActionResult> RecordHealthCheck(Guid instanceId, [FromBody] RecordHealthCheckRequest request, CancellationToken ct)
    {
        var result = await _serviceHealth.RecordHealthCheckAsync(request.OrganizationId, instanceId, request.Success, ct);
        if (!result.IsSuccess) return NotFound(new ProblemDetails { Title = result.Error });
        return NoContent();
    }
}
