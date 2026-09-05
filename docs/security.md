# Security

## Implemented

- ASP.NET Core Identity (password policy: 12+ chars, mixed case, symbol;
  account lockout after 5 failed attempts / 15 min).
- API keys stored as SHA-256 hash + non-secret prefix only — raw key
  returned exactly once at creation (`ApiKeyHasher`, `ApiKey` entity).
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

## Not yet implemented

- Per-action authorization is role-based only — no finer-grained
  resource-level checks (e.g. "can this SRE modify this specific
  organization's incidents") beyond the tenant query filter.
- CSRF tokens aren't exercised anywhere yet because no Razor form exists
  that needs one — the plumbing is registered but unused.
- No rate limit scoping by authenticated user/tenant, only IP or API-key
  prefix (see docs/api.md).
