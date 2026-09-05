using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.PolicyEngine.Application;
using Atlas.Modules.PolicyEngine.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>Thin controller — all persistence and evaluation logic lives in IPolicyManagementService/IPolicyEvaluator.</summary>
[ApiController]
[Route("api/v1/policies")]
[Authorize]
public class PolicyController : ControllerBase
{
    private readonly IPolicyManagementService _policies;
    public PolicyController(IPolicyManagementService policies) => _policies = policies;

    public record CreatePolicyRequest(Guid OrganizationId, string Name, PolicyCondition[] Conditions, PolicyAction Action);
    public record EvaluateRequest(Guid OrganizationId, Dictionary<string, double> Facts);

    [HttpGet]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> List([FromQuery] Guid organizationId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _policies.ListAsync(organizationId, page, pageSize, ct));

    [HttpPost]
    [Authorize(Policy = "Role:PlatformAdmin")]
    public async Task<IActionResult> Create([FromBody] CreatePolicyRequest request, CancellationToken ct)
    {
        var result = await _policies.CreateAsync(request.OrganizationId, request.Name, request.Conditions, request.Action, ct);
        if (!result.Success) return BadRequest(new ProblemDetails { Title = result.Error });
        return Ok(new { policyId = result.Value });
    }

    [HttpPost("{policyId:guid}/deactivate")]
    [Authorize(Policy = "Role:PlatformAdmin")]
    public async Task<IActionResult> Deactivate(Guid policyId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        var result = await _policies.DeactivateAsync(organizationId, policyId, ct);
        if (!result.Success) return NotFound(new ProblemDetails { Title = result.Error });
        return NoContent();
    }

    /// <summary>Real, read-only evaluation against caller-supplied facts — does not itself act on a match (see ActionToolGuard in AIOperations for why).</summary>
    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] EvaluateRequest request, CancellationToken ct)
    {
        var outcomes = await _policies.EvaluateActiveRulesAsync(request.OrganizationId, request.Facts, ct);
        return Ok(outcomes.Select(o => new { policyId = o.Rule.Id, name = o.Rule.Name, matched = o.Matched, action = o.Rule.Action }));
    }
}
