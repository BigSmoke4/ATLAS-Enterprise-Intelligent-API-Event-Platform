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

    [HttpPost]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Define([FromBody] DefineSloRequest request, CancellationToken ct)
    {
        var result = await _slo.DefineSloAsync(request.OrganizationId, request.ServiceId, request.Name, request.MetricType,
            request.TargetValue, request.WindowDuration, ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error });
        return Ok(new { sloId = result.Value });
    }

    [HttpGet("{sloId:guid}")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<SloComplianceResult>> GetCompliance(Guid sloId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var compliance = await _slo.GetComplianceAsync(organizationId, sloId, ct);
        return compliance is null ? NotFound() : Ok(compliance);
    }
}
