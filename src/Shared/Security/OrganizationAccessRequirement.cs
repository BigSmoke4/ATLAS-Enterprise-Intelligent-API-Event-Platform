using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Atlas.Shared.Security;

/// <summary>
/// Resource-level tenant check, on top of the role-based [Authorize]
/// attributes and the EF query filters: verifies the organizationId in the
/// request (query string or route value named "organizationId") matches
/// the caller's "org_id" claim. This is the missing layer flagged in
/// docs/api.md — role + tenant-filter alone don't stop an authenticated
/// SRE from Organization A passing Organization B's organizationId in a
/// query string; the EF filter only protects reads made through
/// ITenantContext-aware DbContexts, not every parameter a controller
/// accepts directly.
///
/// PlatformAdmin bypasses this check by design (a platform-wide operator
/// role that legitimately spans organizations).
/// </summary>
public class OrganizationAccessRequirement : IAuthorizationRequirement { }

public class OrganizationAccessHandler : AuthorizationHandler<OrganizationAccessRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public OrganizationAccessHandler(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OrganizationAccessRequirement requirement)
    {
        if (context.User.IsInRole("PlatformAdmin"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var requestedOrgId = ExtractRequestedOrganizationId(httpContext);
        var callerOrgIdClaim = context.User.FindFirst("org_id")?.Value;

        // No organizationId present in the request at all — nothing for
        // this handler to check; defer to whatever other requirements/role
        // policies already gated the action.
        if (requestedOrgId is null)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (Guid.TryParse(callerOrgIdClaim, out var callerOrgId) && callerOrgId == requestedOrgId)
        {
            context.Succeed(requirement);
        }
        // Deliberately no context.Fail() call with a reason exposed to the
        // client beyond the standard 403 — avoids leaking whether a given
        // organizationId exists at all.

        return Task.CompletedTask;
    }

    private static Guid? ExtractRequestedOrganizationId(HttpContext? httpContext)
    {
        if (httpContext is null) return null;

        if (httpContext.Request.Query.TryGetValue("organizationId", out var queryValue) && Guid.TryParse(queryValue, out var fromQuery))
            return fromQuery;

        if (httpContext.Request.RouteValues.TryGetValue("organizationId", out var routeValue) && Guid.TryParse(routeValue?.ToString(), out var fromRoute))
            return fromRoute;

        return null;
    }
}
