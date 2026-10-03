using Atlas.Modules.Reliability.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

[ApiController]
[Route("api/v1/reliability")]
[Authorize(Policy = "Role:Operator")]
public sealed class ReliabilityController : ControllerBase
{
    private readonly ICircuitBreakerRegistry _circuits;
    public ReliabilityController(ICircuitBreakerRegistry circuits) => _circuits = circuits;

    [HttpGet("circuit-breakers")]
    public IActionResult CircuitBreakers() => Ok(_circuits.SnapshotStates()
        .Select(pair => new { key = pair.Key, state = pair.Value.ToString() }));
}
