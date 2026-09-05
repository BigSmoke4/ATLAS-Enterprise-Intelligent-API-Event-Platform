using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Command-center dashboard. Per the "No Fake Functionality" rule this
/// controller does not synthesize metrics: until Observability's telemetry
/// pipeline (Phase 5) is implemented, the view explicitly renders
/// "No telemetry available." instead of invented numbers.
/// </summary>
public class DashboardController : Controller
{
    public IActionResult Index() => View();
}
