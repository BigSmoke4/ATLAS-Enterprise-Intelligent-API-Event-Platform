namespace Atlas.Shared.Security;

/// <summary>
/// Configuration contract for OAuth/OIDC federation (the <c>Oidc</c> section).
///
/// ATLAS is deliberately explicit about the state of this capability:
/// <list type="bullet">
///   <item>the section is bound and validated at startup, so an operator's
///   intent to federate is never silently ignored — a half-configured provider
///   fails startup with an actionable message;</item>
///   <item>the OpenIdConnect handler itself is a marked extension point and is
///   not registered by this build (see docs/security.md). ATLAS therefore runs
///   on local cookie + API-key authentication until that wiring is added.</item>
/// </list>
/// Treating "configured but not implemented" as a loud startup condition is the
/// point: a security control that silently does nothing is worse than one that
/// is absent.
/// </summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>Provider authority, e.g. <c>https://login.example.com/tenant/v2.0</c>.</summary>
    public string? Authority { get; init; }

    /// <summary>Client (application) id registered with the provider.</summary>
    public string? ClientId { get; init; }

    /// <summary>Client secret; supply it through environment variables or a secret store, never in git.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>Label shown on the (future) external sign-in entry point.</summary>
    public string DisplayName { get; init; } = "Single sign-on";

    /// <summary>Requested scopes; must include <c>openid</c> because this is OpenID Connect, not plain OAuth 2.0.</summary>
    public string[] Scopes { get; init; } = { "openid", "profile", "email" };

    /// <summary>Reject non-HTTPS metadata endpoints (leave true outside local experiments).</summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>
    /// True when the operator supplied any provider identity value. The optional
    /// display/scope/security switches alone do not count as intent.
    /// </summary>
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(Authority) || !string.IsNullOrWhiteSpace(ClientId) || !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>
    /// Startup validation. Returns an empty list when the section is absent
    /// (local authentication only) or complete; otherwise one message per
    /// incomplete setting.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        if (!HasAnyValue) return Array.Empty<string>();

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Authority))
        {
            errors.Add("Oidc:Authority is required once any Oidc setting is present.");
        }
        else if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority) ||
                 (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("Oidc:Authority must be an absolute http(s) URI.");
        }
        else if (RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Oidc:Authority must use https while Oidc:RequireHttpsMetadata is true.");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
            errors.Add("Oidc:ClientId is required once any Oidc setting is present.");

        if (Scopes.Length == 0 || !Scopes.Any(scope => string.Equals(scope, "openid", StringComparison.OrdinalIgnoreCase)))
            errors.Add("Oidc:Scopes must include \"openid\" — this is an OpenID Connect sign-in, not plain OAuth 2.0.");

        return errors;
    }
}
