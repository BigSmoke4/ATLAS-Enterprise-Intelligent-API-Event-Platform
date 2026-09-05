using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.TrafficManagement.Application;
using Atlas.Modules.TrafficManagement.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

[ApiController]
[Route("api/v1/traffic")]
[Authorize(Policy = "Role:SRE")]
public class TrafficController : ControllerBase
{
    private readonly ITrafficRoutingService _routing;
    public TrafficController(ITrafficRoutingService routing) => _routing = routing;

    public record SelectInstanceRequest(Guid OrganizationId, Guid ServiceId, RoutingStrategyType Strategy, int RequestSequenceNumber = 0);

    [HttpPost("select-instance")]
    public async Task<IActionResult> SelectInstance([FromBody] SelectInstanceRequest request, CancellationToken ct)
    {
        var decision = await _routing.SelectInstanceAsync(request.OrganizationId, request.ServiceId,
            new RoutingPolicy(request.Strategy), request.RequestSequenceNumber, ct);
        return Ok(decision);
    }
}
