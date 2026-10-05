using Atlas.Modules.Identity.Domain;
using Atlas.Shared.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.Identity.Presentation;

/// <summary>
/// Registers the OpenID Connect handler when — and only when — the
/// <c>Oidc</c> section is complete (validated at startup by
/// <see cref="OidcOptions.Validate"/>).
///
/// Flow: an anonymous browser hits <c>GET /account/oidc</c>, the handler
/// challenges the configured provider, and on the way back
/// <see cref="MapToLocalAccountAsync"/> resolves the external identity to a
/// local <see cref="AtlasUser"/> and *replaces* the principal with the local
/// one before the Identity cookie is issued. Two consequences worth stating:
///
/// * the cookie therefore carries the local roles/organization claims and the
///   security stamp, so <c>IdentitySessionValidation</c> (per-request
///   revalidation) and the rest of the authorization pipeline work unchanged;
/// * a user the provider authenticates but ATLAS does not know — or knows to be
///   deactivated — is refused, and the browser lands back on the sign-in page
///   with an explanation instead of an opaque 500.
/// </summary>
public static class IdentityOidcExtension
{
    /// <summary>Scheme name of the OIDC handler (the framework's default: "oidc").</summary>
    public const string SchemeName = OpenIdConnectDefaults.AuthenticationScheme;

    /// <summary>Where a refused or failed external sign-in sends the browser.</summary>
    private const string LoginPath = "/account/login";

    public static void AddAtlasOidc(IServiceCollection services, OidcOptions oidc)
    {
        services.AddAuthentication()
            .AddOpenIdConnect(SchemeName, options =>
            {
                options.Authority = oidc.Authority;
                options.ClientId = oidc.ClientId;
                options.ClientSecret = oidc.ClientSecret;
                // Authorization-code flow with PKCE: the browser never sees a token.
                options.ResponseType = "code";
                options.UsePkce = true;
                options.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
                // No token persistence: the platform needs the identity, not the
                // provider's tokens, and it never calls provider APIs on the user's behalf.
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = true;
                // Sign the local principal into the existing Identity cookie.
                options.SignInScheme = IdentityConstants.ApplicationScheme;
                options.Scope.Clear();
                foreach (var scope in oidc.Scopes ?? Array.Empty<string>()) options.Scope.Add(scope);

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = MapToLocalAccountAsync,
                    OnRemoteFailure = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>().CreateLogger("Atlas.Identity.Oidc");
                        logger.LogWarning(context.Failure, "External sign-in failed at the provider.");
                        context.HandleResponse();
                        context.Response.Redirect($"{LoginPath}?ssoError=provider");
                        return Task.CompletedTask;
                    }
                };
            });
    }

    private static async Task MapToLocalAccountAsync(TokenValidatedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Atlas.Identity.Oidc");
        var users = services.GetRequiredService<UserManager<AtlasUser>>();
        var principalFactory = services.GetRequiredService<IUserClaimsPrincipalFactory<AtlasUser>>();

        var email = ExternalIdentityMapper.EmailFrom(context.Principal);
        if (email is null)
        {
            logger.LogWarning("External sign-in refused: the provider returned no e-mail claim.");
            Refuse(context, "notlinked");
            return;
        }

        var user = await users.FindByEmailAsync(email);
        if (!ExternalIdentityMapper.IsUsable(user))
        {
            logger.LogWarning("External sign-in refused for {Email}: no active ATLAS account exists for that address.", email);
            Refuse(context, "notlinked");
            return;
        }

        // Replace the provider's principal with the local one: roles, tenant
        // scope and the security stamp all come from the ATLAS database.
        context.Principal = await principalFactory.CreateAsync(user!);
        logger.LogInformation("External sign-in accepted for {Email} (local account {UserId}).", email, user!.Id);
    }

    private static void Refuse(TokenValidatedContext context, string reason)
    {
        context.HandleResponse();
        context.Response.Redirect($"{LoginPath}?ssoError={reason}");
    }
}
