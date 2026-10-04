using System.Security.Claims;
using Atlas.Modules.Identity.Domain;

namespace Atlas.Web.Controllers;

/// <summary>
/// Shared tenant-scope resolution for MVC pages. The org_id claim is the
/// source of truth: an explicit organizationId (query/body) must match it;
/// when omitted it defaults to the claim so organization members land on
/// their own data without typing GUIDs into the address bar. PlatformAdmin
/// spans organizations by design and may pass any explicit value.
/// </summary>
public static class OrganizationScopeResolver
{
    /// <returns>True when the caller may proceed with the (possibly defaulted) organizationId; false when out of scope.</returns>
    public static bool TryResolve(ClaimsPrincipal user, ref Guid organizationId)
    {
        var claimOrg = Guid.TryParse(user.FindFirst("org_id")?.Value, out var claimed) && claimed != Guid.Empty
            ? claimed
            : (Guid?)null;

        if (user.IsInRole(AtlasRoles.PlatformAdmin))
        {
            if (organizationId == Guid.Empty && claimOrg is { } adminOrg)
                organizationId = adminOrg;
            // An admin without an organization context sees the documented
            // "No telemetry available." state, never a cross-tenant dump.
            return true;
        }

        if (organizationId == Guid.Empty)
        {
            if (claimOrg is null) return false;
            organizationId = claimOrg.Value;
            return true;
        }

        return claimOrg == organizationId;
    }
}
