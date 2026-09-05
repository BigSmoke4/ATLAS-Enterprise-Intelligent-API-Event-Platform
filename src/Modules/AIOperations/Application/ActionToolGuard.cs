namespace Atlas.Modules.AIOperations.Application;

/// <summary>
/// Every Action-kind tool call must pass through here before it runs. This
/// is the enforcement point for "destructive or operational actions require
/// authorization + explicit confirmation + audit logging" — it is not
/// optional prompting guidance, it's a gate the orchestrator cannot skip.
/// No Action tools are registered yet (see AIOperationsModule), so this is
/// exercised by tests but not yet reachable from a live tool.
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
