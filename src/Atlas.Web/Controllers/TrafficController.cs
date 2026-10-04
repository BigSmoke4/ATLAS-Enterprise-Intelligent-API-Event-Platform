using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.TrafficManagement.Application;
using Atlas.Modules.TrafficManagement.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

[ApiController]
[Route("api/v1/traffic")]
[Authorize(Policy = "Role:SRE")]
public class TrafficController : ControllerBase
{
    private readonly ITrafficRoutingService _routing;
    private readonly ITrafficPolicyService _policies;
    private readonly IInstanceTelemetryService _telemetry;
    public TrafficController(ITrafficRoutingService routing, ITrafficPolicyService policies, IInstanceTelemetryService telemetry)
    { _routing = routing; _policies = policies; _telemetry = telemetry; }

    public record SelectInstanceRequest(Guid OrganizationId, Guid ServiceId, RoutingStrategyType Strategy, int RequestSequenceNumber = 0);
    public record ConfigureTrafficRequest(Guid OrganizationId, Guid ServiceId, RoutingStrategyType Strategy, TrafficPolicyMode Mode, TrafficTargetInput[] Targets);

    [HttpGet("policies/{serviceId:guid}")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> GetPolicy(Guid serviceId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var policy = await _policies.GetAsync(organizationId, serviceId, ct);
        return policy is null ? NotFound(new ProblemDetails { Title = "No traffic policy configured." }) : Ok(policy);
    }

    [HttpPut("policies/{serviceId:guid}")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Configure(ConfigureTrafficRequest request, CancellationToken ct)
    {
        if (request.ServiceId == Guid.Empty || request.Targets.Length == 0) return BadRequest(new ProblemDetails { Title = "Service and routing targets are required." });
        if (!User.IsInRole("PlatformAdmin") && User.FindFirst("org_id")?.Value != request.OrganizationId.ToString()) return Forbid();
        var result = await _policies.UpsertAsync(request.OrganizationId, request.ServiceId, request.Strategy, request.Mode, request.Targets, ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error, Detail = result.ErrorCode });
        return Ok(new { policyId = result.Value });
    }

    [HttpPost("select-configured-instance")]
    public async Task<IActionResult> SelectConfiguredInstance([FromBody] SelectInstanceRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        return Ok(await _routing.SelectConfiguredInstanceAsync(request.OrganizationId, request.ServiceId, request.RequestSequenceNumber, ct));
    }

    [HttpPost("select-instance")]
    public async Task<IActionResult> SelectInstance([FromBody] SelectInstanceRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        var decision = await _routing.SelectInstanceAsync(request.OrganizationId, request.ServiceId,
            new RoutingPolicy(request.Strategy), request.RequestSequenceNumber, ct);
        return Ok(decision);
    }

    /// <summary>
    /// Telemetry ingress for the LeastConnections/LatencyBased strategies:
    /// routers/gateways report per-instance gauges (active connections,
    /// average latency). Values are stored exactly as reported — routing
    /// consumes them only while fresher than the staleness window.
    /// </summary>
    [HttpPost("telemetry")]
    public IActionResult ReportTelemetry([FromBody] ReportTelemetryRequest request)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        if (request.Instances.Length == 0)
            return BadRequest(new ProblemDetails { Title = "At least one instance telemetry report is required." });

        foreach (var i in request.Instances)
        {
            if (i.ActiveConnections < 0 || double.IsNaN(i.AvgLatencyMs) || double.IsInfinity(i.AvgLatencyMs) || i.AvgLatencyMs < 0)
                return BadRequest(new ProblemDetails { Title = "ActiveConnections and AvgLatencyMs must be finite, non-negative numbers." });
            _telemetry.Report(request.OrganizationId, request.ServiceId, i.InstanceId, i.ActiveConnections, i.AvgLatencyMs);
        }
        return Ok(new { reported = request.Instances.Length });
    }

    public record InstanceTelemetryInput(Guid InstanceId, int ActiveConnections, double AvgLatencyMs);
    public record ReportTelemetryRequest(Guid OrganizationId, Guid ServiceId, InstanceTelemetryInput[] Instances);

    private bool CanAccess(Guid organizationId) => User.IsInRole("PlatformAdmin") || User.FindFirst("org_id")?.Value == organizationId.ToString();
}
