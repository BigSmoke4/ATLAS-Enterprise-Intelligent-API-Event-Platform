# Security

## Implemented

- ASP.NET Core Identity (password policy: 12+ chars, mixed case, symbol;
  account lockout after 5 failed attempts / 15 min).
- API keys stored as SHA-256 hash + non-secret prefix only — raw key
  returned exactly once at creation (`ApiKeyHasher`, `ApiKey` entity), with
  registration/login/logout and API-key create/revoke endpoints under
  `/api/v1/account`. `X-Api-Key` is converted to a tenant-scoped principal
  by middleware.
- Tenant isolation via EF global query filters (see database.md).
- Role set fixed to the 7 roles in the spec (`AtlasRoles`), policies
  registered per role, and now actually **applied**: every controller
  except `DashboardController` carries `[Authorize]`, with
  state-changing actions (register, declare, transition, create, replay,
  deactivate) further restricted to a specific role policy
  (`Role:SRE`, `Role:OrganizationAdmin`, `Role:PlatformAdmin`,
  `Role:SecurityEngineer`).
- **Security headers** (`SecurityHeadersMiddleware`, `Atlas.Shared.Web`):
  `X-Content-Type-Options`, `X-Frame-Options: DENY`,
  `Referrer-Policy`, `Permissions-Policy`, and a real
  `Content-Security-Policy` (script-src 'self' — safe because no inline
  scripts exist anywhere in `wwwroot/js` per the centralized-JS rule).
  Applied on every response via `app.UseAtlasSecurityHeaders()`.
- **CSRF**: `AddAntiforgery` is registered in `Program.cs`. Applies to any
  future cookie-authenticated Razor `<form>` POST (none exist yet — the
  current Razor pages are read-only) via `@Html.AntiForgeryToken()` +
  `[ValidateAntiForgeryToken]`; API-key-authenticated JSON clients are a
  different auth mechanism and aren't subject to the same CSRF vector.
- **Webhook signature verification**: `Atlas.Shared.Security.WebhookSignatureVerifier`
  — real HMAC-SHA256 + constant-time comparison, unit-tested (tamper,
  wrong-secret, and malformed-signature cases). No concrete webhook
  *receiver* endpoint exists yet in any module (none currently accepts
  third-party webhooks) — this is the verification primitive a future
  receiver (e.g. a CI/CD deployment-notification endpoint) must call.
- **Rate limiting enforced on every live request** (`RateLimitingMiddleware`,
  Redis-backed, atomic Lua-scripted counters).
- **Circuit breaker enforced on outbound calls** (`CircuitBreakerDelegatingHandler`).

- **CSRF**: Razor forms validate antiforgery tokens, and cookie-authenticated
  JSON writes from the console are validated by
  `ValidateAntiforgeryForCookieAuthFilter`; API-key requests are exempt because
  the credential is not attached by the browser automatically.

## Not yet implemented

- **Per-object authorization beyond the tenant.** A caller is authorized for
  their organization (query filters + the `SameOrganization` policy + explicit
  body checks on writes); there is no per-*record* ACL (e.g. "this SRE may edit
  only incidents they declared"). Role policies are the current granularity, and
  the roles themselves are coarse by design.
- **Session revocation / refresh-token rotation.** Cookie sessions are not
  server-side revocable before expiry.
- **OAuth/OIDC connector.** The `Oidc` configuration section *is* implemented
  and enforced at startup (`Atlas.Shared.Security.OidcOptions`, bound in
  `Program.cs`): a partial configuration fails the host with an actionable
  message instead of being ignored, and a complete one logs loudly that
  external sign-in is still disabled. The OpenIdConnect handler itself is a
  marked extension point — see [OAuth/OIDC extension point](#oauthoidc-extension-point).
- **Secret-scanning in CI.** The pipeline fails on committed credentials only
  through the advisory dependency scan; a dedicated secret scanner (or branch
  protection with a pre-commit hook) is an operator-side control.
- **Antiforgery scope.** MVC forms carry `[ValidateAntiForgeryToken]`, and
  `ValidateAntiforgeryForCookieAuthFilter` now enforces the token for
  *cookie-authenticated* JSON writes as well — the console reads the request
  token from a meta tag and sends it as `X-CSRF-TOKEN`. API-key callers are
  exempt by construction (a cross-site page cannot set that header), and the
  skip rules are pinned by unit tests so the exemption cannot silently widen.

## OAuth/OIDC extension point

Local authentication (cookie session for the console, hashed API keys for
machines) is what ATLAS runs on today. Federation is *marked*, not silently
absent, and the marker is executable:

1. **Configuration contract** — `Oidc:Authority`, `Oidc:ClientId`,
   `Oidc:ClientSecret`, `Oidc:DisplayName`, `Oidc:Scopes`,
   `Oidc:RequireHttpsMetadata` (see `.env.example` and `appsettings.json`).
   `OidcOptions.Validate()` runs before `builder.Build()`:
   - section absent → valid, local authentication only;
   - partially filled → startup fails, e.g. *"Oidc:Authority is required once
     any Oidc setting is present"*;
   - complete → the host logs a warning that no handler is registered.
2. **What is missing** — the `Microsoft.AspNetCore.Authentication.OpenIdConnect`
   package and this registration in `Program.cs`:

   ```csharp
   builder.Services.AddAuthentication()
       .AddOpenIdConnect("oidc", options =>
       {
           options.Authority = oidc.Authority;
           options.ClientId = oidc.ClientId;
           options.ClientSecret = oidc.ClientSecret;
           options.ResponseType = "code";
           options.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
           options.SignInScheme = IdentityConstants.ExternalScheme;
           foreach (var scope in oidc.Scopes) options.Scope.Add(scope);
       });
   ```

3. **Why it is not registered yet** — a credential exchange is the easy part;
   the security decision is what happens to the claims afterwards. ATLAS users
   carry an `org_id` claim and one of seven roles, so the connector must be
   accompanied by an explicit provisioning policy (reject unknown identities
   vs. auto-provision to a named role) and a tenant-scoping test. Shipping the
   redirect without that policy would create accounts nobody can audit. The
   decision and its rationale are recorded in
   [ADR-010](decisions/ADR-010-oidc-extension-point.md).
