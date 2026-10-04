using Atlas.Modules.Audit.Application;
using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.IncidentManagement.Application;
using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Modules.Observability.Application;
using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Command-center dashboard. Thin controller: composes read models from
/// four module application services (ServiceRegistry, IncidentManagement,
/// Observability, DeploymentIntelligence) — no queries or business rules
/// live here. Per the "No Fake Functionality" rule, any section without
/// recorded data renders "No telemetry available." instead of a made-up
/// number. Platform-wide operators without an organization context see the
/// same honest empty state rather than a cross-tenant aggregate.
/// </summary>
[Authorize]
public class DashboardController : Controller
{
    private readonly IServiceHealthService _services;
    private readonly IIncidentService _incidents;
    private readonly ISloService _slo;
    private readonly IDeploymentRegressionService _deployments;
    private readonly IDeadLetterService _deadLetters;
    private readonly IAuditQueryService _audit;

    public DashboardController(IServiceHealthService services, IIncidentService incidents,
        ISloService slo, IDeploymentRegressionService deployments,
        IDeadLetterService deadLetters, IAuditQueryService audit)
    {
        _services = services;
        _incidents = incidents;
        _slo = slo;
        _deployments = deployments;
        _deadLetters = deadLetters;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new DashboardViewModel();

        if (Guid.TryParse(User.FindFirst("org_id")?.Value, out var orgId) && orgId != Guid.Empty)
        {
            model.HasOrganizationContext = true;

            var statuses = await _services.GetStatusAsync(orgId, 1, 200, ct);
            model.ServiceTotal = statuses.Count;
            model.ServiceHealthy = statuses.Count(s => s.AggregateHealth == ServiceHealth.Healthy);
            model.ServiceDegraded = statuses.Count(s => s.AggregateHealth == ServiceHealth.Degraded);
            model.ServiceUnhealthy = statuses.Count(s => s.AggregateHealth == ServiceHealth.Unhealthy);
            model.ServiceUnavailable = statuses.Count(s => s.AggregateHealth == ServiceHealth.Unavailable);

            var incidents = await _incidents.GetActiveIncidentsAsync(orgId, page: 1, pageSize: 10, ct: ct);
            model.ActiveIncidents = incidents
                .Where(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.PostmortemComplete)
                .Select(i => new IncidentSummary(i.Id, i.Title, i.Severity.ToString(), i.Status.ToString(), i.StartedAtUtc))
                .ToList();

            var slos = await _slo.ListSlosAsync(orgId, ct);
            var sloSummaries = new List<SloSummary>();
            foreach (var slo in slos.Take(12))
            {
                var compliance = await _slo.GetComplianceAsync(orgId, slo.SloId, ct);
                sloSummaries.Add(new SloSummary(slo.SloId, slo.Name, slo.MetricType.ToString(), slo.TargetValue,
                    compliance?.CurrentValue, compliance?.IsCompliant, compliance?.ErrorBudgetRemainingPercent,
                    compliance?.SampleCount ?? 0));
            }
            model.Slos = sloSummaries;

            var deployments = await _deployments.ListAsync(orgId, null, 1, 8, ct);
            model.RecentDeployments = deployments
                .Select(d => new DeploymentSummary(d.Id, d.ServiceId, d.Version, d.Environment, d.Author, d.DeployedAtUtc, d.Status.ToString()))
                .ToList();

            // Event pipeline signal: un-replayed dead letters (platform-level —
            // DLQ events are intentionally not tenant-attributed).
            var deadLetters = await _deadLetters.ListAsync(topic: null, page: 1, pageSize: 500, ct: ct);
            model.DeadLetterCount = deadLetters.Count(e => !e.Replayed);

            // Security/operational record for this organization (read via the
            // Audit application service — same data as /api/v1/audit).
            var securityEvents = await _audit.ListAsync(orgId, null, null, 1, 5, ct);
            model.SecurityEvents = securityEvents
                .Select(e => new SecurityEventSummary(e.Action, e.ResourceType, e.ActorDisplay, e.CreatedAtUtc))
                .ToList();
        }

        return View(model);
    }
}
