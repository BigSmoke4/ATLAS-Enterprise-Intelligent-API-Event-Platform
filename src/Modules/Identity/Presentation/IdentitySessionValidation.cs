using System.Security.Claims;
using Atlas.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atlas.Modules.Identity.Presentation;

/// <summary>
/// Server-side session revocation for the console cookie.
///
/// A cookie is a bearer token: once issued, nothing on the server can take it
/// back before the ticket expires unless the principal is revalidated. ATLAS
/// revalidates on every request — the user row is re-read (a single indexed
/// lookup on the identity schema) and the cookie's security stamp must still
/// match it, which makes both of these take effect on the caller's next
/// request instead of up to a ticket lifetime later:
///
/// * <c>POST /api/v1/account/sessions/revoke-all</c> and the PlatformAdmin
///   equivalent rotate the security stamp (<c>UserManager.UpdateSecurityStampAsync</c>);
/// * <c>POST /api/v1/account/users/{id}/deactivate</c> clears <c>IsActive</c>,
///   which <see cref="IsStillValid"/> rejects even before the stamp mismatch.
///
/// The cost is one read of the caller's own row per authenticated request. For
/// a control-plane console that is the right trade-off; a high-throughput
/// service would cache the stamp in Redis with a short TTL instead.
///
/// This type lives in the module's Presentation layer so the host can wire it
/// (<c>Program.cs</c>) without referencing Identity's Domain — see the
/// architecture tests that pin that boundary.
/// </summary>
public static class IdentitySessionValidation
{
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var users = services.GetRequiredService<UserManager<AtlasUser>>();
        var identityOptions = services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        var user = await users.GetUserAsync(context.Principal!);
        if (user is not null &&
            IsStillValid(user, context.Principal!, identityOptions.ClaimsIdentity.SecurityStampClaimType))
        {
            return;
        }

        // Deleted, deactivated or revoked: refuse the ticket and clear the cookie
        // so the browser stops presenting it.
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    /// <summary>
    /// Pure decision function behind the revalidation above, kept separate so it
    /// can be unit-tested without a database or a cookie handler.
    /// </summary>
    public static bool IsStillValid(AtlasUser user, ClaimsPrincipal principal, string securityStampClaimType)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(principal);

        if (!user.IsActive) return false;

        var stampInCookie = principal.FindFirstValue(securityStampClaimType);
        return !string.IsNullOrEmpty(user.SecurityStamp) &&
               string.Equals(user.SecurityStamp, stampInCookie, StringComparison.Ordinal);
    }
}
