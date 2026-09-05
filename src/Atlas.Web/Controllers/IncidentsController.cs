using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.IncidentManagement.Application;
using Atlas.Modules.IncidentManagement.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

[Authorize]
public class IncidentsController : Controller
{
    private readonly IIncidentService _incidents;
    public IncidentsController(IIncidentService incidents) => _incidents = incidents;

    public record DeclareIncidentRequest(Guid OrganizationId, string Title, IncidentSeverity Severity, DateTimeOffset StartedAtUtc, Guid[] AffectedServiceIds);
    public record TransitionRequest(Guid OrganizationId, IncidentStatus Target, string Note);

    [HttpGet("/Incidents")]
    public async Task<IActionResult> Index([FromQuery] Guid organizationId, CancellationToken ct)
    {
        var incidents = organizationId == Guid.Empty
            ? Array.Empty<Incident>()
            : (await _incidents.GetActiveIncidentsAsync(organizationId, 1, 50, ct)).ToArray();
        return View(incidents);
    }

    [HttpGet("/api/v1/incidents")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<Incident>>> ListJson([FromQuery] Guid organizationId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _incidents.GetActiveIncidentsAsync(organizationId, page, pageSize, ct));

    [HttpPost("/api/v1/incidents")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Declare([FromBody] DeclareIncidentRequest request, CancellationToken ct)
    {
        var result = await _incidents.DeclareIncidentAsync(request.OrganizationId, request.Title, request.Severity,
            request.StartedAtUtc, request.AffectedServiceIds, ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error });
        return Ok(new { incidentId = result.Value });
    }

    [HttpPost("/api/v1/incidents/{incidentId:guid}/transition")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Transition(Guid incidentId, [FromBody] TransitionRequest request, CancellationToken ct)
    {
        var result = await _incidents.TransitionAsync(request.OrganizationId, incidentId, request.Target, request.Note, ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error, Detail = result.ErrorCode });
        return NoContent();
    }
}
