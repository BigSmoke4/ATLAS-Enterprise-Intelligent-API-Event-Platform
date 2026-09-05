# Threat Model

| Threat | Mitigation status |
|---|---|
| Auth bypass | ASP.NET Core Identity + role policies, now enforced with `[Authorize]` on 10 of 11 controllers (implemented) |
| Tenant isolation failure | EF query filters (implemented; app-layer double-check still recommended for critical actions) |
| Credential/API key theft | Keys stored hashed only, shown once (implemented) |
| SQL injection | EF Core parameterized queries throughout (implemented by construction; no raw SQL anywhere) |
| XSS | Razor's default output encoding (implemented by framework default) + a real CSP restricting script-src to 'self' (implemented) |
| CSRF | Antiforgery service registered (implemented); no form yet exercises it (no gap in practice — no vulnerable form exists) |
| API abuse / DoS | Real, enforced rate limiting on every request (`RateLimitingMiddleware` + `RedisRateLimitStore`) and a real circuit breaker on outbound calls (implemented) |
| Webhook spoofing | `WebhookSignatureVerifier` (real HMAC-SHA256, unit-tested) exists as the verification primitive; no webhook receiver endpoint exists yet to apply it to (planned) |
| Prompt injection / tool abuse | Read/Action tool separation is real; `ActionToolGuard` denies unauthorized/unconfirmed action calls (implemented); only one action tool exists so far (`DeactivatePolicy`) |
| Kafka poisoning / malicious payloads | Consumer retries with backoff then dead-letters on repeated failure (implemented); no payload schema validation before dispatch yet (planned) |
| SSRF | `HealthCheckProberService` now makes outbound HTTP calls to registered instance addresses — these come from `ServiceInstance.HostAndPort`, which is operator-registered data (via an authorized `[Authorize(Policy="Role:SRE")]` endpoint), not arbitrary user input, which is the relevant SSRF mitigation for this specific call site. No other URL-by-user-input fetch exists. |

Anything still marked planned is a real gap, not a false sense of
security — treat this table as a to-do list, not a compliance claim.
