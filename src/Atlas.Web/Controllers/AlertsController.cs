using Atlas.Web.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Active alert surface. Alerts are <em>derived</em> from recorded state
/// (registry health, SLO compliance, breaker states, consumer lag, dead-letter
/// backlog) rather than stored as a separate mutable feed, so an alert can
/// never outlive the condition that produced it — and every alert carries the
/// evidence it was derived from.
///
/// A result of <c>alerts: []</c> with <c>hasEvidence: true</c> means "checked
/// and clear". When a source could not be read it is named in
/// <c>evidenceSource</c> with the reason.
/// </summary>
[ApiController]
[Route("api/v1/alerts")]
[Authorize]
public sealed class AlertsController : ControllerBase
{
    private readonly AlertReadModel _alerts;
    public AlertsController(AlertReadModel alerts) => _alerts = alerts;

    [HttpGet]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<AlertSnapshot>> List([FromQuery] Guid organizationId, CancellationToken ct = default)
        => Ok(await _alerts.BuildAsync(organizationId, ct));
}
