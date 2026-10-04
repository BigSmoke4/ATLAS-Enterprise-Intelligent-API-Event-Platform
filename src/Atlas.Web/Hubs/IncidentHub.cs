using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Atlas.Web.Hubs;

[Authorize]
public sealed class IncidentHub : Hub
{
    public Task JoinOrganization(string organizationId)
    {
        var callerOrganization = Context.User?.FindFirst("org_id")?.Value;
        if (!Guid.TryParse(organizationId, out var requested) ||
            (Context.User?.IsInRole("PlatformAdmin") != true && callerOrganization != requested.ToString()))
            throw new HubException("Organization access denied.");
        return Groups.AddToGroupAsync(Context.ConnectionId, $"organization:{requested}");
    }
}
