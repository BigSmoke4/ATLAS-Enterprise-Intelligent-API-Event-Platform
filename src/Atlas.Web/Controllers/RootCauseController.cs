using Atlas.Modules.AIOperations.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

[ApiController]
[Route("api/v1/incidents")]
[Authorize(Policy = "Role:SRE")]
public sealed class RootCauseController : ControllerBase
{
    private readonly IRootCauseAnalysisService _analysis;
    public RootCauseController(IRootCauseAnalysisService analysis) => _analysis = analysis;

    [HttpGet("{incidentId:guid}/root-cause-analysis")]
    public async Task<IActionResult> Analyze(Guid incidentId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!User.IsInRole("PlatformAdmin") && User.FindFirst("org_id")?.Value != organizationId.ToString()) return Forbid();
        return Ok(await _analysis.AnalyzeIncidentAsync(organizationId, incidentId, ct));
    }
}
