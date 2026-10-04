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

    /// <summary>SLO catalog for the organization: definitions only — compliance is fetched per SLO so a slow window never blocks the list.</summary>
    [HttpGet]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<SloDefinitionDto>>> List([FromQuery] Guid organizationId, CancellationToken ct = default)
        => Ok(await _slo.ListSlosAsync(organizationId, ct));

    /// <summary>Compliance + error budget for every SLO of the organization, using live request telemetry where it exists.</summary>
    [HttpGet("compliance")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> Compliance([FromQuery] Guid organizationId, CancellationToken ct = default)
    {
        var definitions = await _slo.ListSlosAsync(organizationId, ct);
        var results = new List<object>(definitions.Count);

        foreach (var definition in definitions.Take(50))
        {
            var compliance = await _slo.GetComplianceAsync(organizationId, definition.SloId, ct);
            results.Add(new
            {
                definition.SloId,
                definition.ServiceId,
                definition.Name,
                metricType = definition.MetricType.ToString(),
                definition.TargetValue,
                windowMinutes = definition.WindowDuration.TotalMinutes,
                hasData = compliance?.HasData ?? false,
                evidenceSource = compliance?.EvidenceSource ?? "none",
                currentValue = compliance?.CurrentValue,
                isCompliant = compliance?.IsCompliant,
                errorBudgetRemainingPercent = compliance?.ErrorBudgetRemainingPercent,
                burnRatePerHour = compliance?.BurnRatePerHour,
                sampleCount = compliance?.SampleCount ?? 0
            });
        }

        return Ok(results);
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
