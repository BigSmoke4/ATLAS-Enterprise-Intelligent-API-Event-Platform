using System.Security.Claims;
using Atlas.Modules.AIOperations.Application;
using Atlas.Modules.Audit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Exposes AiOperationsAssistant. /ask is evidence-grounded and refuses to
/// fabricate ("Insufficient evidence."). /actions is the ONLY way to
/// invoke an Action-kind tool (e.g. DeactivatePolicy) — it always requires
/// explicitConfirmation=true in the request body AND the PlatformAdmin
/// role; ActionToolGuard is checked inside ExecuteActionAsync regardless of
/// what this controller passes, so this endpoint cannot bypass it. Every
/// action attempt (success or denial) is recorded via IAuditLogger.
/// </summary>
[ApiController]
[Route("api/v1/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly AiOperationsAssistant _assistant;
    private readonly IAuditLogger _audit;
    public AiController(AiOperationsAssistant assistant, IAuditLogger audit)
    {
        _assistant = assistant;
        _audit = audit;
    }

    public record AskRequest(Guid OrganizationId, string Question, string[] ToolNames, Dictionary<string, string>? ToolArguments = null);
    public record ExecuteActionRequest(Guid OrganizationId, string ToolName, Dictionary<string, string> Arguments, bool ExplicitConfirmation);

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        // The assistant's tools are tenant-scoped by the organizationId in
        // the request body — verify the caller may actually act within that
        // tenant, exactly like every other org-scoped endpoint.
        if (!CanAccess(request.OrganizationId)) return Forbid();
        var answer = await _assistant.AnswerAsync(request.OrganizationId, request.Question, request.ToolNames, request.ToolArguments, ct);
        return Ok(answer);
    }

    [HttpPost("actions")]
    [Authorize(Policy = "Role:PlatformAdmin")]
    public async Task<IActionResult> ExecuteAction([FromBody] ExecuteActionRequest request, CancellationToken ct)
    {
        // Reaching this action at all already proves Role:PlatformAdmin via
        // the attribute above — that satisfies ActionToolGuard's
        // "callerIsAuthorized" check. ExplicitConfirmation must still be
        // true in the request body; the guard is re-checked inside
        // ExecuteActionAsync regardless of what's passed here.
        var result = await _assistant.ExecuteActionAsync(request.OrganizationId, request.ToolName, request.Arguments,
            callerIsAuthorized: true, explicitConfirmationGiven: request.ExplicitConfirmation, ct);

        await _audit.RecordAsync(
            // ClaimTypes.NameIdentifier is the platform-wide actor id: the Identity
            // cookie pipeline and ApiKeyAuthenticationMiddleware both put the user
            // id there, so an AI action is attributed to a human in every case.
            actorUserId: ActorUserId(),
            actorDisplay: User.Identity?.Name ?? "unknown",
            organizationId: request.OrganizationId,
            action: $"ai.action.{request.ToolName}",
            resourceType: "AiAction",
            resourceId: request.ToolName,
            correlationId: Guid.NewGuid(),
            afterJson: result.Summary,
            ct: ct);

        if (!result.Success) return UnprocessableEntity(new ProblemDetails { Title = result.Summary });
        return Ok(result);
    }

    private Guid? ActorUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private bool CanAccess(Guid organizationId)
        => User.IsInRole("PlatformAdmin") || User.FindFirst("org_id")?.Value == organizationId.ToString();
}
