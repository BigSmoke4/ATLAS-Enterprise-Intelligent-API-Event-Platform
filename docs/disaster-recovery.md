# Disaster Recovery

Targets below are **commitments the deployment must meet**, each tied to a
mechanism and a verification step. The repository supplies the application-side
prerequisites (reviewed migrations in git, environment-driven configuration,
stateless web instances, dependency-aware health probes); the storage-side
mechanisms (WAL archiving, broker replication, backup storage) are provisioning
work, and a target is only real once a drill has reproduced it. Numbers here are
deliberately concrete so a drill can fail.

## What is durable, and where

| Data | Store | Loss impact |
|---|---|---|
| Identity, organizations, API/route policy, policies, incidents, deployments, audit trail | PostgreSQL | Business state — must be recoverable |
| Request telemetry aggregates (14-day retention) | PostgreSQL | Dashboards/SLO history for the window is lost; no business impact |
| Dead-letter records with payload + failure reason | PostgreSQL (EventPlatform schema) | Failed events can no longer be replayed |
| Cache entries, rate-limit counters, distributed locks | Redis | None — designed to be reconstructible; counters reset |
| Event log (topics) | Kafka | Consumers resume from committed offsets; un-acknowledged events replay if the topic survives |

## Targets

| Scenario | RPO target | RTO target | Mechanism | Verified by |
|---|---|---|---|---|
| Web instance lost | 0 | ≤ 5 min | Stateless instances behind a load balancer; in-flight telemetry buffer (≤ 50 000 observations) is lost, bounded and counted | Kill an instance during a k6 run; assert no 5xx after drain |
| PostgreSQL instance lost | ≤ 5 min | ≤ 1 h | Continuous WAL archiving + PITR; restore to a fresh instance, run `scripts/migrate.sh` (idempotent for already-applied migrations), re-point `ConnectionStrings:Postgres` | Quarterly restore drill into an empty environment, then `/health/ready` |
| PostgreSQL data corruption / operator error | ≤ 5 min | ≤ 4 h | PITR to a timestamp just before the incident | Same drill, restoring to a point in time |
| Redis lost | n/a | ≤ 15 min | Redis is disposable: restart the instance. Counters reset, so limits are conservative on cold start (documented trade-off), cache refills from PostgreSQL | Restart Redis under load; assert requests still succeed and `atlas.ratelimit.store.failures` is the only signal |
| Kafka broker lost | ≤ topic retention / last acknowledgement, whichever is shorter | ≤ 2 h | Consumer groups resume from committed offsets; dead-letter payloads live in PostgreSQL, so replay survives loss of a DLQ topic | Stop the broker, record a deployment (must still succeed), restart, assert consumption resumes |
| Availability-zone / cluster loss | ≤ 15 min | ≤ 8 h | Warm standby in a second zone (provisioning-specific); restore databases, deploy the last release tag, replay from Kafka where retention allows | Full failover exercise |
| Release regression | n/a | ≤ 30 min | Roll forward with the previous image tag; migrations are additive and already applied, never reverted in place | Rollback rehearsal per release train |

Telemetry retention is a deliberate data-loss boundary: one-minute aggregates are
deleted after 14 days, so "recovering" telemetry older than that is out of
scope by design. The **audit trail is append-only and never deleted by the
application**, so it is the one table whose retention must be decided explicitly
by the operator (archival, not deletion).

## Runbook (PostgreSQL restore)

1. Provision a fresh PostgreSQL instance at the target version.
2. Restore the most recent base backup, then replay WAL to the chosen recovery
   point (`recovery_target_time`).
3. Point `ConnectionStrings:Postgres` at the restored instance (environment
   variable / secret store — never a committed file).
4. Run `scripts/migrate.sh` with the migration identity; it applies only
   migrations the restored database has not seen.
5. Confirm `/health/ready` reports PostgreSQL healthy, then re-enable traffic.
6. Record the drill result (achieved RPO/RTO, surprises) in the operations log —
   an untested target is an assumption.

### Running the drill

`scripts/dr-drill.sh` turns the runbook's first four steps into one command and
prints the measured result instead of an estimate:

```bash
PGHOST=… PGPORT=5432 PGDATABASE=atlas PGUSER=… PGPASSWORD=… scripts/dr-drill.sh
```

It records row counts for one table per schema, takes a `pg_dump` backup, drops
every application schema (the simulated total loss), restores the backup, and
re-checks the counts, printing the restore time as the measured RTO. A count
mismatch fails the script — a restore that silently drops rows is the failure
mode a drill exists to find. It is destructive by construction and therefore
never runs in this repository's CI (the pipeline shares one ephemeral database
with the other suites); run it in a maintenance window against a database you
are allowed to destroy, and record the output in the operations log.

## Known gaps (honest)

- No automated failover is shipped: the platform exposes readiness and health,
  but a managed PostgreSQL/Kafka offering or an orchestrator-driven failover is
  the deployment's job.
- The restore drill **is** automated (`scripts/dr-drill.sh`, above) but cannot
  run in this repository's CI: it destroys the database, and the pipeline shares
  one ephemeral instance with the other suites. The targets above stay estimates
  until an operator runs the script in a maintenance window and records the
  measured RTO/RPO.
- Backup age is not currently alerted on by the platform — it belongs to the
  backup system, and is listed as an operator responsibility in
  `docs/deployment.md`.
