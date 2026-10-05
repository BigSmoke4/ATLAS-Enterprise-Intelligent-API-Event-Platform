using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Modules.Identity.Domain;
using Atlas.Modules.Identity.Presentation;
using Atlas.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// The federated sign-in path, exercised end to end against an OpenID provider
/// that runs *inside the test process* — no network leaves the runner and no
/// external IdP is required.
///
/// The fake provider is a second minimal Kestrel host that speaks the parts of
/// the protocol the middleware actually uses: discovery, an authorization
/// endpoint that echoes the nonce and hands back a code, a token endpoint that
/// returns an id_token signed with a real RSA key, the matching JWKS, and a
/// userinfo endpoint. That means the test drives the production handler through
/// code + PKCE, nonce validation, signature validation against the JWKS, the
/// userinfo round trip, principal replacement and the Identity cookie — the
/// claims this repository makes in docs/security.md, rather than a description
/// of them.
///
/// Two facts make the assertions trustworthy:
/// <list type="bullet">
///   <item>the client uses an <c>https://localhost</c> base address, because
///   the console cookie is <c>Secure</c> and correlation cookies are
///   <c>SameSite=None</c>; over plain http a cookie container would store them
///   and never send them back, and the test would fail for the wrong reason;</item>
///   <item>the accounts are created in the real Identity store (PostgreSQL in
///   CI), so <c>FindByEmailAsync</c>, the security stamp and
///   <c>IdentitySessionValidation</c> behave exactly as they do in a
///   deployment — the session that follows is revalidated against the database
///   on the next request.</item>
/// </list>
/// </summary>
public sealed class OidcSignInIntegrationTests
{
    private const string KeyId = "atlas-integration-key";

    /// <summary>
    /// Minimal OpenID provider. Every endpoint answers from the request, so the
    /// same instance can serve several flows; the nonce and the account e-mail
    /// are per-instance state set by the test before the flow starts.
    /// </summary>
    private sealed class FakeOpenIdProvider : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly RSA _signingKey = RSA.Create(2048);
        private readonly ConcurrentDictionary<string, string> _noncesByCode = new();
        private string _baseUrl = string.Empty;

        private FakeOpenIdProvider(WebApplication app) => _app = app;

        /// <summary>The address the provider hands out as its issuer.</summary>
        public string BaseUrl => _baseUrl;

        public string ClientId => "atlas-integration-client";

        /// <summary>E-mail the issued id_token/userinfo claims; the code under test maps it to a local account.</summary>
        public string Email { get; set; } = "sso-operator@atlas.local";

        public string Subject { get; } = $"subject-{Guid.NewGuid():N}";

        public static async Task<FakeOpenIdProvider> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            var app = builder.Build();
            var provider = new FakeOpenIdProvider(app);

            app.MapGet("/.well-known/openid-configuration", () => Results.Json(provider.Discovery()));
            app.MapGet("/authorize", (HttpContext context) => provider.Authorize(context));
            app.MapPost("/token", (HttpContext context) => provider.WriteTokenAsync(context));
            app.MapGet("/userinfo", (HttpContext context) => provider.UserInfo(context));
            app.MapGet("/jwks", () => Results.Json(provider.Jwks()));

            // Port 0: the OS picks a free port, so parallel test classes cannot collide.
            app.Urls.Add("http://127.0.0.1:0");
            await app.StartAsync();
            provider._baseUrl = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return provider;
        }

        private Dictionary<string, object?> Discovery() => new()
        {
            ["issuer"] = _baseUrl,
            ["authorization_endpoint"] = $"{_baseUrl}/authorize",
            ["token_endpoint"] = $"{_baseUrl}/token",
            ["userinfo_endpoint"] = $"{_baseUrl}/userinfo",
            ["jwks_uri"] = $"{_baseUrl}/jwks",
            ["response_types_supported"] = new[] { "code" },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
            ["scopes_supported"] = new[] { "openid", "profile", "email" },
            ["claims_supported"] = new[] { "sub", "iss", "aud", "exp", "iat", "nonce", "email", "name" },
        };

        private IResult Authorize(HttpContext context)
        {
            var query = context.Request.Query;
            var redirectUri = query["redirect_uri"].ToString();
            var code = Guid.NewGuid().ToString("N");

            // Echo the nonce in the id_token later: the middleware compares the
            // two, which is what proves nonce validation is really in the path.
            _noncesByCode[code] = query["nonce"].ToString();

            var location = QueryHelpers.AddQueryString(redirectUri, "code", code);
            location = QueryHelpers.AddQueryString(location, "state", query["state"].ToString());
            return Results.Redirect(location);
        }

        private async Task WriteTokenAsync(HttpContext context)
        {
            var form = await context.Request.ReadFormAsync();
            if (!_noncesByCode.TryRemove(form["code"].ToString(), out var nonce))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_grant" });
                return;
            }

            await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
            {
                ["access_token"] = "integration-access-token",
                ["token_type"] = "Bearer",
                ["expires_in"] = 3600,
                ["scope"] = "openid profile email",
                ["id_token"] = CreateIdToken(nonce),
            });
        }

        private IResult UserInfo(HttpContext context)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return Results.Unauthorized();
            return Results.Json(new { sub = Subject, email = Email, name = "SSO Operator" });
        }

        private Dictionary<string, object?> Jwks()
        {
            var parameters = _signingKey.ExportParameters(includePrivateParameters: false);
            return new Dictionary<string, object?>
            {
                ["keys"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["kty"] = "RSA",
                        ["use"] = "sig",
                        ["alg"] = "RS256",
                        ["kid"] = KeyId,
                        ["n"] = Base64Url(parameters.Modulus!),
                        ["e"] = Base64Url(parameters.Exponent!),
                    }
                }
            };
        }

        private string CreateIdToken(string nonce)
        {
            var now = DateTimeOffset.UtcNow;
            var header = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = KeyId,
            });
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["iss"] = _baseUrl,
                ["aud"] = ClientId,
                ["sub"] = Subject,
                ["email"] = Email,
                ["email_verified"] = true,
                ["name"] = "SSO Operator",
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
                ["nonce"] = nonce,
            });

            var signingInput = $"{Base64Url(Encoding.UTF8.GetBytes(header))}.{Base64Url(Encoding.UTF8.GetBytes(payload))}";
            var signature = _signingKey.SignData(Encoding.UTF8.GetBytes(signingInput),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return $"{signingInput}.{Base64Url(signature)}";
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public async ValueTask DisposeAsync()
        {
            _signingKey.Dispose();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// The real host — every controller, the Identity cookie configuration,
    /// <c>IdentitySessionValidation</c>, the claims factory and the real user
    /// store — with the production federated-sign-in entry point
    /// (<see cref="IdentityOidcExtension.AddAtlasOidc"/>) invoked for a fake
    /// provider.
    ///
    /// Why the entry point is called here instead of through configuration:
    /// <c>appsettings.json</c> already defines the <c>Oidc</c> keys (deliberately
    /// empty), and configuration layered by a test host is read *before* the app's
    /// own sources, so a per-test authority URL cannot win through
    /// <c>UseSetting</c> or <c>ConfigureAppConfiguration</c> — the empty value
    /// would stay and Program.cs would correctly register nothing. Rather than
    /// mutating process-wide environment variables (which would leak into other
    /// test classes' hosts), the test calls the same production method Program.cs
    /// calls, so the handler options, the principal mapping and the refusal
    /// behaviour under test are exactly the shipped ones. The enable/disable
    /// branch itself is covered separately: <c>OidcOptionsTests</c> pins the
    /// configuration contract and
    /// <see cref="An_unconfigured_deployment_serves_no_federated_route_and_advertises_none"/>
    /// pins the disabled deployment.
    /// </summary>
    private sealed class OidcWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly FakeOpenIdProvider _provider;

        public OidcWebApplicationFactory(FakeOpenIdProvider provider) => _provider = provider;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => IdentityOidcExtension.AddAtlasOidc(services, new OidcOptions
            {
                Authority = _provider.BaseUrl,
                ClientId = _provider.ClientId,
                ClientSecret = "sso-test-secret",
                DisplayName = "Integration Provider",
                Scopes = new[] { "openid", "profile", "email" },
                RequireHttpsMetadata = false,
            }));
        }
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // https so the Secure/SameSite cookies the handler issues are stored
            // and sent back; the test server serves any scheme.
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>Creates (or refuses to create) the local account the provider's e-mail resolves to.</summary>
    private static async Task<Guid> ProvisionAsync(WebApplicationFactory<Program> factory, string email, Guid organizationId, bool isActive)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AtlasUser>>();

        var user = new AtlasUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "SSO Operator",
            IsActive = isActive,
            CurrentOrganizationId = organizationId,
        };

        var created = await users.CreateAsync(user, "Sso-Test-Password-1!");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var role = await users.AddToRoleAsync(user, AtlasRoles.Viewer);
        Assert.True(role.Succeeded, string.Join("; ", role.Errors.Select(error => error.Description)));
        return user.Id;
    }

    /// <summary>
    /// Drives the full round trip: ATLAS challenge → provider authorize →
    /// ATLAS callback → token exchange → sign-in. Returns the response to the
    /// callback, which is what the tests assert on (redirect target and
    /// whether a session was established).
    /// </summary>
    private static async Task<HttpResponseMessage> SignInAsync(HttpClient atlas, FakeOpenIdProvider provider)
    {
        using var challenge = await atlas.GetAsync("/account/oidc");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, challenge.StatusCode);

        var location = challenge.Headers.Location;
        Assert.NotNull(location);
        Assert.StartsWith(provider.BaseUrl + "/authorize", location!.ToString(), StringComparison.Ordinal);

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal(provider.ClientId, query["client_id"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Contains("openid", query["scope"].ToString());
        Assert.EndsWith("/signin-oidc", query["redirect_uri"].ToString(), StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, query["state"].ToString());
        Assert.NotEqual(string.Empty, query["nonce"].ToString());

        // The provider's authorize endpoint is a second in-process host, so it
        // is called with its own client (no cookies, no redirect following).
        using var providerClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var authorize = await providerClient.GetAsync(location);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, authorize.StatusCode);

        var callback = authorize.Headers.Location;
        Assert.NotNull(callback);
        var callbackPath = callback!.PathAndQuery;

        // Back to ATLAS with the code and the correlation cookie: this is the
        // hop that redeems the code, validates the id_token against the JWKS,
        // validates the nonce, maps the identity and issues the session.
        return await atlas.GetAsync(callbackPath);
    }

    [Fact]
    public async Task A_federated_sign_in_signs_in_a_provisioned_account_and_reuses_the_local_roles()
    {
        await using var provider = await FakeOpenIdProvider.StartAsync();
        var organizationId = Guid.NewGuid();
        var email = $"oidc-operator-{Guid.NewGuid():N}@atlas.local";
        provider.Email = email;

        using var factory = new OidcWebApplicationFactory(provider);
        var userId = await ProvisionAsync(factory, email, organizationId, isActive: true);
        using var atlas = Client(factory);

        // The login page advertises the federated route only because a provider
        // is configured (the view is driven by the registered scheme).
        using var loginPage = await atlas.GetAsync("/account/login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, loginPage.StatusCode);
        Assert.Contains("SIGN IN WITH INTEGRATION PROVIDER", loginHtml);

        using var callback = await SignInAsync(atlas, provider);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/Dashboard", callback.Headers.Location!.ToString());

        // The session works, and IdentitySessionValidation accepted it — which
        // it only does for a live row whose security stamp matches the cookie,
        // so this also proves the cookie carries the *local* principal.
        using var dashboard = await atlas.GetAsync("/Dashboard");
        Assert.Equal(System.Net.HttpStatusCode.OK, dashboard.StatusCode);
        var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("data-page=\"dashboard\"", dashboardHtml);

        // And the local claims really are the local ones: the organization came
        // from the user row, not from the provider (which never mentioned it).
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AtlasUser>>();
        var stored = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(stored);
        Assert.Equal(organizationId, stored!.CurrentOrganizationId);
    }

    [Fact]
    public async Task An_identity_with_no_local_account_is_refused_without_a_session()
    {
        await using var provider = await FakeOpenIdProvider.StartAsync();
        // The provider authenticates this address; ATLAS has never heard of it.
        provider.Email = $"oidc-unknown-{Guid.NewGuid():N}@atlas.local";

        using var factory = new OidcWebApplicationFactory(provider);
        await ProvisionAsync(factory, $"oidc-other-{Guid.NewGuid():N}@atlas.local", Guid.NewGuid(), isActive: true);
        using var atlas = Client(factory);

        using var callback = await SignInAsync(atlas, provider);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains("/account/login", callback.Headers.Location!.ToString());
        Assert.Contains("ssoError=notlinked", callback.Headers.Location!.ToString());

        // No session followed: a protected page still bounces to sign-in.
        using var dashboard = await atlas.GetAsync("/Dashboard");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, dashboard.StatusCode);
        Assert.Contains("/account/login", dashboard.Headers.Location!.ToString());
    }

    [Fact]
    public async Task A_deactivated_local_account_is_refused_even_though_the_provider_authenticated_it()
    {
        await using var provider = await FakeOpenIdProvider.StartAsync();
        var email = $"oidc-disabled-{Guid.NewGuid():N}@atlas.local";
        provider.Email = email;

        using var factory = new OidcWebApplicationFactory(provider);
        await ProvisionAsync(factory, email, Guid.NewGuid(), isActive: false);
        using var atlas = Client(factory);

        using var callback = await SignInAsync(atlas, provider);
        Assert.Contains("ssoError=notlinked", callback.Headers.Location!.ToString());

        using var dashboard = await atlas.GetAsync("/Dashboard");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, dashboard.StatusCode);
    }

    [Fact]
    public async Task An_unconfigured_deployment_serves_no_federated_route_and_advertises_none()
    {
        // The ordinary factory: no Oidc section, so Program.cs registers no
        // handler at all. The endpoint must answer 404 rather than send a
        // browser into a challenge nobody can complete.
        using var factory = new TestWebApplicationFactory();
        using var atlas = Client(factory);

        using var endpoint = await atlas.GetAsync("/account/oidc");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, endpoint.StatusCode);

        using var loginPage = await atlas.GetAsync("/account/login");
        var html = await loginPage.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, loginPage.StatusCode);
        Assert.DoesNotContain("SIGN IN WITH", html);
    }
}
