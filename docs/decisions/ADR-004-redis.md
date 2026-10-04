# ADR-004: Redis for cache, locks and distributed rate limiting

**Status:** Accepted — implemented.

## Context
Three different needs share one requirement: correctness across multiple web
instances. Route-policy lookups must not hit PostgreSQL on every request, some
operations need a cross-instance lock, and rate-limit counters must be shared or
every node would allow its own full quota.

## Decision
Redis 7 via StackExchange.Redis, reached only through three abstractions in
`src/Shared/Contracts`:

- **`ICacheService`** — tenant-agnostic cache of route-policy snapshots
  (`RoutePolicyCacheEntry`, 30 s TTL) with statistics (`ICacheStatistics`) that
  feed `atlas.cache.hits` / `atlas.cache.misses`. Invalidation is explicit:
  configuring a route writes through/evicts the affected entry.
- **`IDistributedLock`** — a single-owner lock with expiry for operations that
  must not interleave.
- **`IRateLimitStore`** — atomic counters implemented as **Lua scripts** so a
  read-modify-write decision cannot race between instances. All four algorithms
  (fixed window, token bucket, sliding window, leaky bucket) return a single
  `<allowed>:<level>` reply, which the store parses strictly.

Every store call is wrapped so a Redis outage is a *decision*, not an
exception: rate limiting fails open (documented as a deliberate availability
over strictness trade-off, with `atlas.ratelimit.store.failures` exported so the
condition is visible), while a malformed script reply is treated as fail-closed
because it indicates a code/contract defect rather than an outage.

## Consequences
- Rate limiting is correct across nodes; the integration suite asserts atomic
  behaviour under concurrency against a real Redis.
- Redis becomes a dependency of the *availability* path — hence the explicit
  failure policy and the health probe that reports it in `/health/ready`.
- Cache entries carry TTLs and the platform can prove hit/miss behaviour
  instead of assuming the cache helps.

## Alternatives considered
- **In-process memory cache/locks** — rejected: incorrect the moment a second
  instance runs, which is the deployment target.
- **PostgreSQL advisory locks and counters for rate limiting** — rejected: it
  would put the request path's hot writes onto the database that must stay
  available for business data.
