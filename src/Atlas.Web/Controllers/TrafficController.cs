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
    private readonly ITrafficPolicyService _policies;
    public TrafficController(ITrafficRoutingService routing, ITrafficPolicyService policies) { _routing = routing; _policies = policies; }

    public record SelectInstanceRequest(Guid OrganizationId, Guid ServiceId, RoutingStrategyType Strategy, int RequestSequenceNumber = 0);
    public record ConfigureTrafficRequest(Guid OrganizationId, Guid ServiceId, RoutingStrategyType Strategy, TrafficPolicyMode Mode, TrafficTargetInput[] Targets);

    [HttpGet("policies/{serviceId:guid}")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> GetPolicy(Guid serviceId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var policy = await _policies.GetAsync(organizationId, serviceId, ct);
        return policy is null ? NotFound(new ProblemDetails { Title = "No traffic policy configured." }) : Ok(policy);
    }

    [HttpPut("policies/{serviceId:guid}")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Configure(ConfigureTrafficRequest request, CancellationToken ct)
    {
        if (request.ServiceId == Guid.Empty || request.Targets.Length == 0) return BadRequest(new ProblemDetails { Title = "Service and routing targets are required." });
        if (!User.IsInRole("PlatformAdmin") && User.FindFirst("org_id")?.Value != request.OrganizationId.ToString()) return Forbid();
        var result = await _policies.UpsertAsync(request.OrganizationId, request.ServiceId, request.Strategy, request.Mode, request.Targets, ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error, Detail = result.ErrorCode });
        return Ok(new { policyId = result.Value });
    }

    [HttpPost("select-configured-instance")]
    public async Task<IActionResult> SelectConfiguredInstance([FromBody] SelectInstanceRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        return Ok(await _routing.SelectConfiguredInstanceAsync(request.OrganizationId, request.ServiceId, request.RequestSequenceNumber, ct));
    }

    [HttpPost("select-instance")]
    public async Task<IActionResult> SelectInstance([FromBody] SelectInstanceRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        var decision = await _routing.SelectInstanceAsync(request.OrganizationId, request.ServiceId,
            new RoutingPolicy(request.Strategy), request.RequestSequenceNumber, ct);
        return Ok(decision);
    }

    private bool CanAccess(Guid organizationId) => User.IsInRole("PlatformAdmin") || User.FindFirst("org_id")?.Value == organizationId.ToString();
}
