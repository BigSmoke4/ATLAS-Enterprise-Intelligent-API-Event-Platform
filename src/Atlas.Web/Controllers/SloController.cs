using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.Observability.Application;
using Atlas.Modules.Observability.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

[ApiController]
[Route("api/v1/slo")]
[Authorize]
public class SloController : ControllerBase
{
    private readonly ISloService _slo;
    public SloController(ISloService slo) => _slo = slo;

    public record DefineSloRequest(Guid OrganizationId, Guid ServiceId, string Name, SloMetricType MetricType, double TargetValue, TimeSpan WindowDuration);
    public record RecordOutcomeRequest(Guid OrganizationId, Guid ServiceId, SloMetricType MetricType, bool Success, string? DeploymentVersion);
    public record RecordLatencyRequest(Guid OrganizationId, Guid ServiceId, SloMetricType MetricType, double ValueMs, string? DeploymentVersion);

    [HttpPost]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Define([FromBody] DefineSloRequest request, CancellationToken ct)
    {
        var result = await _slo.DefineSloAsync(request.OrganizationId, request.ServiceId, request.Name, request.MetricType,
            request.TargetValue, request.WindowDuration, ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error });
        return Ok(new { sloId = result.Value });
    }

    [HttpPost("samples/outcome")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> RecordOutcome(RecordOutcomeRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        await _slo.RecordOutcomeAsync(request.OrganizationId, request.ServiceId, request.MetricType, request.Success, request.DeploymentVersion, ct);
        return Accepted();
    }

    [HttpPost("samples/latency")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> RecordLatency(RecordLatencyRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId) || request.ValueMs < 0) return BadRequest(new ProblemDetails { Title = "Invalid organization or latency." });
        await _slo.RecordLatencyAsync(request.OrganizationId, request.ServiceId, request.MetricType, request.ValueMs, request.DeploymentVersion, ct);
        return Accepted();
    }

    [HttpGet("{sloId:guid}")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<SloComplianceResult>> GetCompliance(Guid sloId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var compliance = await _slo.GetComplianceAsync(organizationId, sloId, ct);
        return compliance is null ? NotFound() : Ok(compliance);
    }

    private bool CanAccess(Guid organizationId) => User.IsInRole("PlatformAdmin") || User.FindFirst("org_id")?.Value == organizationId.ToString();
}
