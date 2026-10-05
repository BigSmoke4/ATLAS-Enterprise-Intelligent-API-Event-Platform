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
- **CSRF**: `AddAntiforgery` is registered in `Program.cs`; MVC forms carry
  `[ValidateAntiForgeryToken]` and `ValidateAntiforgeryForCookieAuthFilter`
  additionally requires the token for cookie-authenticated JSON writes from
  the console (the client sends `X-CSRF-TOKEN`); API-key callers are exempt
  because a cross-site page cannot set that header.
- **Webhook signature verification**: `Atlas.Shared.Security.WebhookSignatureVerifier`
  — real HMAC-SHA256 + constant-time comparison, unit-tested (tamper,
  wrong-secret, and malformed-signature cases). No concrete webhook
  *receiver* endpoint exists yet in any module (none currently accepts
  third-party webhooks) — this is the verification primitive a future
  receiver (e.g. a CI/CD deployment-notification endpoint) must call.
- **Rate limiting enforced on every live request** (`RateLimitingMiddleware`,
  Redis-backed, atomic Lua-scripted counters).
- **Circuit breaker enforced on outbound calls** (`CircuitBreakerDelegatingHandler`).
- **Revocable sessions.** The console cookie is revalidated on every request
  (`IdentitySessionValidation`, wired through `CookieAuthenticationEvents.OnValidatePrincipal`):
  the user row is re-read and the cookie's security stamp must still match it.
  `POST /api/v1/account/sessions/revoke-all` rotates the caller's stamp and
  signs them out; `POST /api/v1/account/users/{id}/sessions/revoke-all`
  (PlatformAdmin) revokes another account's sessions;
  `POST /api/v1/account/users/{id}/deactivate|reactivate` (PlatformAdmin)
  turns access off and on, refusing self-deactivation. Every one of those calls
  is written to the audit ledger. Tickets expire after 12 hours and slide while
  the session is used. The cost is one indexed user read per authenticated
  request — a deliberate control-plane trade-off; a high-throughput service
  would cache the stamp with a short TTL instead.

- **Federated sign-in (OAuth/OIDC).** When the `Oidc` section is complete,
  `IdentityOidcExtension.AddAtlasOidc` registers a real
  `Microsoft.AspNetCore.Authentication.OpenIdConnect` handler (scheme `oidc`,
  authorization-code flow with PKCE, no token persistence) and
  `GET /account/oidc` starts the round trip; the login page renders the SSO
  button only when that handler is registered. Federation is **authentication
  only**: the external identity is resolved to a local account by verified
  e-mail claim (`email` → `ClaimTypes.Email` → `preferred_username` → `upn`;
  `sub` is deliberately not used — it is provider-local), and the request
  proceeds only when that account exists **and is active**. Roles, `org_id` and
  the security stamp always come from this database, so
  `IdentitySessionValidation` and the whole authorization pipeline behave
  unchanged. There is no auto-provisioning: an unknown identity is refused with
  an explicit redirect to `/account/login?ssoError=notlinked`. The policy and
  its rationale are in the section below and in ADR-010.
- **Per-incident ownership.** Incidents carry their declarer
  (`Incident.DeclaredByUserId`), and `IncidentAccessPolicy` decides writes per
  *record*, not only per role: `PlatformAdmin`/`OrganizationAdmin` may act on
  any incident in scope, an `SRE` may act on the incidents they declared, and an
  incident with no declarer on record is closed to a plain `SRE` (fail closed —
  the privileged roles stay the escape hatch). Transition, root cause and
  postmortem answer `403` with the reason when the policy denies, and the rule is
  unit-tested independently of the database. This is the platform's first
  per-object ACL and the pattern the other aggregates copy.

- **Secret scanning runs in CI as two independent layers.**
  `scripts/secret-scan.sh` fails the build on a curated deny-list of credential
  formats (AWS access key ids, GitHub tokens, Anthropic/OpenAI/Slack/Google keys,
  PEM private keys) and on a literal value under a secret-shaped key in
  configuration or `.env.example` — deterministic, fast, and reviewed in the same
  pull request as the code. A second job runs `gitleaks`
  (`ghcr.io/gitleaks/gitleaks:v8.30.1`) over the **full commit history** with its
  own rules and entropy heuristics, configured by the narrow reviewed
  `.gitleaks.toml` allowlist, and republishes findings as check annotations via
  `scripts/gitleaks-annotations.py`.

## Not yet implemented

- **Per-object authorization beyond incidents.** Incidents now carry a
  per-record rule (see "Per-incident ownership" above). The other aggregates are
  authorized by tenant scope plus role: a caller is authorized for their
  organization (query filters + the `SameOrganization` policy + explicit body
  checks on writes), with no per-record ACL (e.g. "this developer may edit only
  the routes they created"). Extending the incident pattern across the remaining
  resources is deliberate, incremental work rather than a silent gap.
- **Refresh-token rotation.** *Session revocation is implemented* (see
  "Revocable sessions" above): an operator can end every session for an
  account and it takes effect on the next request. What is deliberately absent
  is token *rotation* for machine clients: API keys are long-lived until
  revoked, and there is no refresh-token grant, because the platform issues no
  access tokens today. With federated sign-in enabled (see below), rotation is
  the identity provider's concern.
- **Append-only audit trail at the database level.** `AuditDbContext` rejects
  `Modified`/`Deleted` entries in `SaveChanges` (and `docs/architecture.md`
  records the callers), but the deployment role can still issue raw SQL. The
  operational hardening step — run once per environment — is a grant change
  against the audit schema:

  ```sql
  REVOKE UPDATE, DELETE ON audit."AuditEntries" FROM atlas_app;
  ```

  Nothing in the application performs those statements, so this is safe to
  apply; it is listed here rather than assumed, because a bug in the DbContext
  guard would otherwise be the only line of defence.
- **Push protection (operator-side).** CI scans a secret *after* it has reached
  the remote. GitHub's push protection — a repository setting, not code — refuses
  the push outright; `docs/deployment.md` lists enabling it in the production
  checklist. Everything else about secret scanning is implemented (below).
- **Antiforgery scope.** MVC forms carry `[ValidateAntiForgeryToken]`, and
  `ValidateAntiforgeryForCookieAuthFilter` now enforces the token for
  *cookie-authenticated* JSON writes as well — the console reads the request
  token from a meta tag and sends it as `X-CSRF-TOKEN`. API-key callers are
  exempt by construction (a cross-site page cannot set that header), and the
  skip rules are pinned by unit tests so the exemption cannot silently widen.

## Federated sign-in (OAuth/OIDC)

Local authentication (cookie session for the console, hashed API keys for
machines) remains the default; federation activates only when the operator
completes the `Oidc` section.

1. **Configuration contract** — `Oidc:Authority`, `Oidc:ClientId`,
   `Oidc:ClientSecret`, `Oidc:DisplayName`, `Oidc:Scopes`,
   `Oidc:RequireHttpsMetadata` (see `.env.example` and `appsettings.json`).
   `OidcOptions.Validate()` runs before `builder.Build()`:
   - section absent → valid, local authentication only;
   - partially filled → the host fails with an actionable message, e.g.
     *"Oidc:Authority is required once any Oidc setting is present"*; an
     `http://` authority is refused while `RequireHttpsMetadata` is true;
   - complete → `IdentityOidcExtension.AddAtlasOidc` registers the handler and
     the host logs the entry point.
2. **What the handler does** (`src/Modules/Identity/Presentation/IdentityOidcExtension.cs`)
   — authorization-code flow with PKCE, `SaveTokens = false`, scheme `oidc`,
   `SignInScheme = IdentityConstants.ApplicationScheme`. On `OnTokenValidated`
   the external principal is **replaced** by the local one produced by
   `IUserClaimsPrincipalFactory<AtlasUser>`. That single decision keeps the
   platform's security model intact: the cookie carries the local user id,
   roles, `org_id` and security stamp, so authorization policies, tenant query
   filters and per-request session revocation behave exactly as they do for
   password sign-in. `OnRemoteFailure` redirects to
   `/account/login?ssoError=provider`.
3. **Provisioning policy — none, deliberately.** `ExternalIdentityMapper`
   resolves a verified e-mail claim and `IsUsable` requires an existing, active
   local account; unknown or disabled identities are refused with
   `ssoError=notlinked`. Auto-provisioning from a federated claim would create
   accounts nobody reviewed, and group→role mapping is provider-specific, so
   both are left to a deliberate, reviewed change. [ADR-010](decisions/ADR-010-oidc-extension-point.md)
   is the record, updated by this implementation.
4. **Verified end to end** — `OidcSignInIntegrationTests` runs the handler
   against an OpenID provider hosted *inside* the test process (discovery, an
   authorization endpoint that echoes the nonce, a token endpoint returning an
   RSA-signed `id_token`, the matching JWKS and a userinfo endpoint). It asserts
   the challenge carries `response_type=code`, a PKCE `code_challenge` and the
   configured scopes and client id; that a provisioned account completes the
   exchange, passes nonce and signature validation and arrives at `/Dashboard`
   with a working session (`IdentitySessionValidation` accepts it, which is only
   possible if the cookie carries the local principal); that an identity with no
   local account is refused with `ssoError=notlinked` and no session; that a
   *deactivated* account is refused even though the provider authenticated it;
   and that an unconfigured deployment answers `404` and renders no SSO button.
   `ExternalIdentityMapperTests` pins the claim precedence (including the
   refusal to trust `sub`) and `OidcOptionsTests` the configuration contract.
   Still not covered by tests, because it needs a real provider: provider-side
   logout propagation and group→role mapping, neither of which is implemented.
