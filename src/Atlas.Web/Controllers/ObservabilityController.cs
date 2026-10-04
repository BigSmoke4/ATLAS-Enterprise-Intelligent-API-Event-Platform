using Atlas.Modules.Observability.Application;
using Atlas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Razor "Observability" page: tenant-scoped SLO catalog with compliance and
/// error budgets derived from recorded metric samples. Read-only, thin —
/// all data comes from ISloService. Tenant scope follows OrganizationScopeResolver.
/// </summary>
[Authorize]
public sealed class ObservabilityController : Controller
{
    private const int MaxSlosRendered = 25;
    private readonly ISloService _slo;
    public ObservabilityController(ISloService slo) => _slo = slo;

    [HttpGet("/Observability")]
    public async Task<IActionResult> Index([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!OrganizationScopeResolver.TryResolve(User, ref organizationId)) return Forbid();

        var model = new ObservabilityViewModel { HasOrganizationContext = organizationId != Guid.Empty };
        if (!model.HasOrganizationContext) return View(model);

        var rows = new List<SloDetailRow>();
        foreach (var slo in (await _slo.ListSlosAsync(organizationId, ct)).Take(MaxSlosRendered))
        {
            var compliance = await _slo.GetComplianceAsync(organizationId, slo.SloId, ct);
            rows.Add(new SloDetailRow(slo.SloId, slo.ServiceId, slo.Name, slo.MetricType.ToString(),
                slo.TargetValue, slo.WindowDuration, compliance?.CurrentValue, compliance?.IsCompliant,
                compliance?.ErrorBudgetRemainingPercent, compliance?.SampleCount ?? 0));
        }
        model.Slos = rows;
        return View(model);
    }
}
