# ADR-006: Distributed rate limiting, four algorithms behind one store

**Status:** Accepted — implemented.

## Context
Different APIs need different admission control: a login endpoint wants a cheap
fixed window, a partner API wants burst tolerance (token bucket), a fair-share
endpoint wants smooth averaging (sliding window), and a downstream service needs
shaping rather than dropping (leaky bucket). The counters must be shared by all
web instances or the effective limit becomes `limit × instances`.

## Decision
One `IRateLimitStore` (Redis, Lua) with four algorithms and six scopes, selected
**per route** by stored configuration rather than by code:

- Algorithms: `FixedWindow`, `TokenBucket`, `SlidingWindow`, `LeakyBucket`.
- Scopes: `Ip`, `User`, `ApiKey`, `Tenant`, `Endpoint`, `Global` — the key
  namespace is derived from the scope plus the resolved identity, so two
  organizations never share a counter.
- Policies are read from the API-route configuration through
  `IRoutePolicyProvider` (memoised per request, cached in Redis for 30 s) and
  applied by the rate-limiting middleware on the real request path, before the
  endpoint runs.
- Every algorithm is implemented as an atomic Lua script returning
  `<allowed>:<level>`; callers never do read-then-write.

## Consequences
- Changing a route's limit/scope/algorithm is configuration (`PUT
  /api/v1/traffic/policies/{serviceId}` for routing, route configuration for
  limits), not a deployment.
- Rejections are counted (`atlas.ratelimit.rejections`) and returned as `429`
  with the standard problem response; Redis unavailability fails **open** with a
  counted failure metric, a documented availability-over-strictness choice.
- The Lua scripts are part of the tested surface: unit tests cover each
  algorithm's decision function, and an integration test drives the store
  concurrently against a real Redis.

## Alternatives considered
- **ASP.NET Core's built-in rate limiter alone** — rejected: it is per-process,
  and the platform's requirement is a distributed limit driven by stored
  per-route policy.
- **Middleware reading configuration directly** — rejected: the limit would then
  be a code concern and every change a deploy.
