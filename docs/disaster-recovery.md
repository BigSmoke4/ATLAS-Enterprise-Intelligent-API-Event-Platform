# Disaster Recovery (planned targets, not yet load-tested)

- PostgreSQL: `pg_dump` scheduled backup + WAL archiving recommended;
  restore via `pg_restore` into a fresh instance, then re-point
  `ConnectionStrings:Postgres`.
- Redis: treated as ephemeral/cache — data loss on failure should not lose
  business state (rate-limit counters may reset, which is an accepted
  trade-off, not a correctness bug, since limits are designed to be
  conservative on cold start).
- Kafka: consumer groups resume from last committed offset; DLQ topics hold
  events that failed processing for manual/automated replay once
  EventPlatform is implemented.
- RPO/RTO targets are not defined here as concrete numbers because they
  have not been derived from an actual load test in this environment — do
  that before publishing them as commitments.
