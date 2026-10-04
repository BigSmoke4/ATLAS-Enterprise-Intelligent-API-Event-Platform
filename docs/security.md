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
  server-side revocable before expiry, and OAuth/OIDC federation is a marked
  extension point rather than connector code — see the README limitations list.
- **Secret-scanning in CI.** The pipeline fails on committed credentials only
  through the advisory dependency scan; a dedicated secret scanner (or branch
  protection with a pre-commit hook) is an operator-side control.
- **Antiforgery scope.** MVC forms carry `[ValidateAntiForgeryToken]`, and
  `ValidateAntiforgeryForCookieAuthFilter` now enforces the token for
  *cookie-authenticated* JSON writes as well — the console reads the request
  token from a meta tag and sends it as `X-CSRF-TOKEN`. API-key callers are
  exempt by construction (a cross-site page cannot set that header), and the
  skip rules are pinned by unit tests so the exemption cannot silently widen.
