namespace Atlas.Modules.AIOperations.Application;

/// <summary>
/// Every Action-kind tool call must pass through here before it runs. This
/// is the enforcement point for "destructive or operational actions require
/// authorization + explicit confirmation + audit logging" — it is not
/// optional prompting guidance, it's a gate the orchestrator cannot skip.
/// DeactivatePolicy is a live Action tool reachable through
/// <c>POST /api/v1/ai/actions</c> (PlatformAdmin + explicitConfirmation), and
/// every attempt — allowed or denied — is written to the audit ledger.
/// </summary>
public class ActionToolGuard
{
    public sealed record AuthorizationDecision(bool Authorized, string Reason);

    public AuthorizationDecision Authorize(IAtlasTool tool, bool callerIsAuthorized, bool explicitConfirmationGiven)
    {
        if (tool.Kind == ToolKind.Read)
            return new AuthorizationDecision(true, "Read tools do not require confirmation.");

        if (!callerIsAuthorized)
            return new AuthorizationDecision(false, "Caller lacks the required role/permission for this action.");

        if (!explicitConfirmationGiven)
            return new AuthorizationDecision(false, "Action tools require explicit user confirmation before execution.");

        return new AuthorizationDecision(true, "Authorized action, explicitly confirmed.");
    }
}
