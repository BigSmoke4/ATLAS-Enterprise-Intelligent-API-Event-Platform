using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.PolicyEngine.Application;
using Atlas.Modules.PolicyEngine.Domain;
using Atlas.Shared.Contracts;
using Atlas.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>Thin controller — all persistence and evaluation logic lives in IPolicyManagementService/IPolicyEvaluator.</summary>
[ApiController]
[Route("api/v1/policies")]
[Authorize]
public class PolicyController : ControllerBase
{
    private readonly IPolicyManagementService _policies;
    private readonly IAuditSink _audit;
    public PolicyController(IPolicyManagementService policies, IAuditSink audit) { _policies = policies; _audit = audit; }

    public record CreatePolicyRequest(Guid OrganizationId, string Name, PolicyCondition[] Conditions, PolicyAction Action);
    public record EvaluateRequest(Guid OrganizationId, Dictionary<string, double> Facts);
    public record CreatePolicyVersionRequest(Guid OrganizationId, PolicyCondition[] Conditions, PolicyAction Action);

    [HttpGet]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> List([FromQuery] Guid organizationId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDirection = null, CancellationToken ct = default)
    {
        if (!SortQuery.TryResolve(PolicySorting.Rules, sortBy, sortDirection, out var direction, out var error)) return error!;
        var rules = await _policies.ListAsync(organizationId, page, pageSize, ct, sortBy, direction);
        return Ok(rules.Select(PolicyRuleDto.From).ToArray());
    }

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
        if (!CanAccess(organizationId)) return Forbid();
        var result = await _policies.DeactivateAsync(organizationId, policyId, ct);
        if (!result.Success) return NotFound(new ProblemDetails { Title = result.Error });
        await _audit.RecordAsync(new AuditRecord(UserId(), User.Identity?.Name ?? "unknown", organizationId, "policy.deactivated", "PolicyRule", policyId.ToString(), Guid.NewGuid(), AfterJson: "{\"active\":false}"), ct);
        return NoContent();
    }

    [HttpPost("{policyId:guid}/versions")]
    [Authorize(Policy = "Role:PlatformAdmin")]
    public async Task<IActionResult> CreateVersion(Guid policyId, CreatePolicyVersionRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        var result = await _policies.CreateVersionAsync(request.OrganizationId, policyId, request.Conditions, request.Action, ct);
        if (!result.Success) return BadRequest(new ProblemDetails { Title = result.Error });
        await _audit.RecordAsync(new AuditRecord(UserId(), User.Identity?.Name ?? "unknown", request.OrganizationId, "policy.version.created", "PolicyRule", result.Value.ToString(), Guid.NewGuid()), ct);
        return StatusCode(StatusCodes.Status201Created, new { policyId = result.Value });
    }

    [HttpPost("{policyId:guid}/activate")]
    [Authorize(Policy = "Role:PlatformAdmin")]
    public async Task<IActionResult> Activate(Guid policyId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!CanAccess(organizationId)) return Forbid();
        var result = await _policies.ActivateAsync(organizationId, policyId, ct);
        if (!result.Success) return NotFound(new ProblemDetails { Title = result.Error });
        await _audit.RecordAsync(new AuditRecord(UserId(), User.Identity?.Name ?? "unknown", organizationId, "policy.activated", "PolicyRule", policyId.ToString(), Guid.NewGuid(), AfterJson: "{\"active\":true}"), ct);
        return NoContent();
    }

    /// <summary>Real, read-only evaluation against caller-supplied facts — does not itself act on a match (see ActionToolGuard in AIOperations for why).</summary>
    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] EvaluateRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        var outcomes = await _policies.EvaluateActiveRulesAsync(request.OrganizationId, request.Facts, ct);
        return Ok(outcomes.Select(o => new { policyId = o.Rule.Id, name = o.Rule.Name, version = o.Rule.Version, matched = o.Matched, action = o.Rule.Action }));
    }

    private Guid? UserId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    private bool CanAccess(Guid organizationId) => User.IsInRole("PlatformAdmin") || User.FindFirst("org_id")?.Value == organizationId.ToString();
}
