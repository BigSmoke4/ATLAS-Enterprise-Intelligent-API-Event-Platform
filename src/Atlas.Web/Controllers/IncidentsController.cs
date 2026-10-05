using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.IncidentManagement.Application;
using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Atlas.Web.Hubs;

namespace Atlas.Web.Controllers;

[Authorize]
public class IncidentsController : Controller
{
    private readonly IIncidentService _incidents;
    private readonly IHubContext<IncidentHub> _hub;
    public IncidentsController(IIncidentService incidents, IHubContext<IncidentHub> hub) { _incidents = incidents; _hub = hub; }

    public record DeclareIncidentRequest(Guid OrganizationId, string Title, IncidentSeverity Severity, DateTimeOffset StartedAtUtc, Guid[] AffectedServiceIds);
    public record TransitionRequest(Guid OrganizationId, IncidentStatus Target, string Note);
    public record RootCauseRequest(Guid OrganizationId, string RootCause, string Mitigation);
    public record PostmortemRequest(Guid OrganizationId, string PostmortemUrl);

    /// <summary>Razor view. Real data only; tenant scope resolved from the caller's org claim (see OrganizationScopeResolver).</summary>
    [HttpGet("/Incidents")]
    public async Task<IActionResult> Index([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        var incidents = organizationId == Guid.Empty
            ? Array.Empty<Incident>()
            : (await _incidents.GetActiveIncidentsAsync(organizationId, 1, 50, null, null, ct)).ToArray();
        return View(incidents);
    }

    [HttpGet("/api/v1/incidents")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<IncidentDto>>> ListJson([FromQuery] Guid organizationId,
        [FromQuery] IncidentStatus? status = null, [FromQuery] IncidentSeverity? severity = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDirection = null, CancellationToken ct = default)
    {
        if (!SortQuery.TryResolve(IncidentSorting.Incidents, sortBy, sortDirection, out var direction, out var error)) return error!;
        var incidents = await _incidents.GetActiveIncidentsAsync(organizationId, page, pageSize, status, severity, ct, sortBy, direction);
        return Ok(incidents.Select(IncidentDto.From).ToArray());
    }

    [HttpGet("/api/v1/incidents/{incidentId:guid}")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> Get(Guid incidentId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var incident = await _incidents.GetAsync(organizationId, incidentId, ct);
        return incident is null ? NotFound(new ProblemDetails { Title = "Incident not found." }) : Ok(IncidentDto.From(incident));
    }

    [HttpPost("/api/v1/incidents")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Declare([FromBody] DeclareIncidentRequest request, CancellationToken ct)
    {
        var result = await _incidents.DeclareIncidentAsync(request.OrganizationId, request.Title, request.Severity,
            request.StartedAtUtc, request.AffectedServiceIds, UserId(), ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error });
        await _hub.Clients.Group($"organization:{request.OrganizationId}").SendAsync("incident-updated", new { incidentId = result.Value, status = IncidentStatus.Detected }, ct);
        return Ok(new { incidentId = result.Value });
    }

    [HttpPost("/api/v1/incidents/{incidentId:guid}/transition")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Transition(Guid incidentId, [FromBody] TransitionRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        // Per-incident authorization runs before the state machine: an SRE may
        // transition what they declared, privileged roles anything in scope.
        var existing = await _incidents.GetAsync(request.OrganizationId, incidentId, ct);
        if (existing is null) return NotFound(new ProblemDetails { Title = "Incident not found." });
        if (!IncidentAccessPolicy.CanAct(existing, UserId(), IsPrivilegedIncidentRole()))
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "You are not allowed to change this incident.",
                Detail = IncidentAccessPolicy.ExplainDenial(existing)
            });

        var result = await _incidents.TransitionAsync(request.OrganizationId, incidentId, request.Target, request.Note, UserId(), ct);
        if (!result.IsSuccess) return result.ErrorCode == "NOT_FOUND" ? NotFound(new ProblemDetails { Title = result.Error }) : BadRequest(new ProblemDetails { Title = result.Error, Detail = result.ErrorCode });
        await _hub.Clients.Group($"organization:{request.OrganizationId}").SendAsync("incident-updated", new { incidentId, status = request.Target }, ct);
        return NoContent();
    }

    [HttpPost("/api/v1/incidents/{incidentId:guid}/root-cause")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> RootCause(Guid incidentId, RootCauseRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        var incident = await _incidents.GetAsync(request.OrganizationId, incidentId, ct);
        if (incident is null) return NotFound(new ProblemDetails { Title = "Incident not found." });
        if (!IncidentAccessPolicy.CanAct(incident, UserId(), IsPrivilegedIncidentRole())) return DeniedIncidentWrite(incident);
        var result = await _incidents.RecordRootCauseAsync(request.OrganizationId, incidentId, request.RootCause, request.Mitigation, UserId(), ct);
        return result.IsSuccess ? NoContent() : BadRequest(new ProblemDetails { Title = result.Error, Detail = result.ErrorCode });
    }

    [HttpPost("/api/v1/incidents/{incidentId:guid}/postmortem")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Postmortem(Guid incidentId, PostmortemRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        var incident = await _incidents.GetAsync(request.OrganizationId, incidentId, ct);
        if (incident is null) return NotFound(new ProblemDetails { Title = "Incident not found." });
        if (!IncidentAccessPolicy.CanAct(incident, UserId(), IsPrivilegedIncidentRole())) return DeniedIncidentWrite(incident);
        var result = await _incidents.CompletePostmortemAsync(request.OrganizationId, incidentId, request.PostmortemUrl, UserId(), ct);
        return result.IsSuccess ? NoContent() : BadRequest(new ProblemDetails { Title = result.Error, Detail = result.ErrorCode });
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>
    /// Roles that may act on any incident in scope: the incident-management
    /// escape hatch when the declarer is unavailable (see IncidentAccessPolicy).
    /// </summary>
    private bool IsPrivilegedIncidentRole() =>
        User.IsInRole(Atlas.Modules.Identity.Domain.AtlasRoles.PlatformAdmin) ||
        User.IsInRole(Atlas.Modules.Identity.Domain.AtlasRoles.OrganizationAdmin);

    private ObjectResult DeniedIncidentWrite(Incident incident) =>
        StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Title = "You are not allowed to change this incident.",
            Detail = IncidentAccessPolicy.ExplainDenial(incident)
        });
    private bool CanAccess(Guid organizationId) => User.IsInRole("PlatformAdmin") || User.FindFirst("org_id")?.Value == organizationId.ToString();
}
