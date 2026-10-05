using System.Security.Claims;
using Atlas.Modules.Identity.Domain;

namespace Atlas.Modules.Identity.Presentation;

/// <summary>
/// The decision half of external (OIDC) sign-in, kept pure so it can be
/// unit-tested without a token, a provider or a database.
///
/// ATLAS federates *authentication* only: the identity provider proves who the
/// operator is, and ATLAS then resolves that identity to a **local** account.
/// Roles, organization scope and the active flag stay in the local database, so
/// a change at the provider can never grant platform permissions, and removing
/// access here takes effect immediately (see IdentitySessionValidation).
/// </summary>
public static class ExternalIdentityMapper
{
    /// <summary>
    /// Claim types ATLAS accepts as the account identifier, in order of
    /// preference. <c>email</c> first because it is the claim every OIDC
    /// provider is required to return for a verified address; the others cover
    /// the common Azure AD (<c>preferred_username</c>/<c>upn</c>) and
    /// generic-JWT (<c>sub</c> is *not* used — it is provider-local, not an
    /// account address) conventions.
    /// </summary>
    private static readonly string[] EmailClaims =
    {
        "email", ClaimTypes.Email, "preferred_username", "upn"
    };

    /// <summary>First usable e-mail address in the external principal, or null.</summary>
    public static string? EmailFrom(ClaimsPrincipal? principal)
    {
        if (principal is null) return null;

        foreach (var claimType in EmailClaims)
        {
            var value = principal.FindFirst(claimType)?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    /// <summary>
    /// An external identity may only sign in when a local account exists and is
    /// active. There is deliberately no auto-provisioning: an account created by
    /// an unknown IdP user would bypass the administrative provisioning step.
    /// </summary>
    public static bool IsUsable(AtlasUser? localAccount) => localAccount is { IsActive: true };
}
