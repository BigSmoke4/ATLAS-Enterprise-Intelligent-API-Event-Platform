# ADR-011: Transactional outbox for at-least-once publication

**Status:** Accepted — implemented.

## Context

`DeploymentRegressionService.RecordDeploymentAsync` recorded a deployment and
then published `DeploymentRecorded` to Kafka. The order was deliberate (the
business fact must not depend on a broker), but it left a real hole: if Kafka was
unreachable — or the process died between the commit and the publish — the
deployment was durable and the event was simply gone. Every consumer of that
event (audit trail, canary analysis, downstream automation) silently missed it,
and the only trace was a log line.

The platform's rule is that a documented gap is better than a hidden one, but the
event feed is not a place where "usually delivered" is acceptable: recording a
deployment is exactly the moment other systems need to know.

## Decision

Use a **transactional outbox**, scoped to the module that publishes.

- `OutboxMessage` (`DeploymentIntelligence.Domain`) carries the complete event
  envelope (topic, type, version, event id, correlation/causation, producer,
  occurrence time) plus the serialized payload. It is validated by
  `EventContractValidator` **at write time**, so an unpublishable event cannot be
  recorded at all.
- `RecordDeploymentAsync` adds the deployment and the outbox row in the **same
  `SaveChanges`**: one transaction, so the event exists if and only if the
  business fact does.
- `OutboxRelayService` (hosted service) publishes pending rows. Claiming is a
  single conditional `UPDATE` that leases the row by moving `NextAttemptAtUtc`
  into the future; zero rows affected means another instance owns it.
- Delivery is **at least once**: a row is marked sent only after the broker
  accepted it, so a crash in between republishes. This is safe because consumers
  are idempotent by construction — the unique `(ConsumerGroup, EventId)` index
  from ADR-007 is the other half of the contract.
- Failures are counted, the error is stored (truncated), and retries use
  exponential backoff (`Outbox:InitialBackoff` → `Outbox:MaxBackoff`). After
  `Outbox:MaxAttempts` the row is **abandoned**: kept in the table for
  inspection, never retried automatically, counted by `atlas.outbox.abandoned`.
- The relay is idle (and logs once) when no broker is configured; it never
  pretends to deliver.

## Consequences

- A broker outage now delays events instead of losing them, and the backlog is
  visible as rows plus `atlas.events.published{path="outbox"}` with a flat rate.
- The relay adds a second writer to the module's DbContext and a polling loop;
  both are bounded (`BatchSize`, `PollInterval`) and cancellation-aware, and the
  pass is exposed as `RunOnceAsync` so integration tests drive the real logic
  instead of a timer.
- The outbox table is infrastructure state, not tenant data: it is not
  tenant-filtered (the relay must see every pending message) and carries no
  `xmin` token, because the lease is the concurrency control (see
  `docs/database.md`).
- Only the deployment publisher uses the outbox today. Adopting it elsewhere is
  a per-module change with its own migration; the pattern, the metrics and the
  tests are the template.
- `OutboxMessages` grows with unpublished traffic. The expected steady state is
  empty; a retention/cleanup job for *sent* rows is deliberately not shipped yet,
  because deleting rows the relay might still touch is a bigger risk than disk
  growth in a control plane, and `SentAtUtc` makes a safe cleanup possible later.

## Alternatives considered

- **Publish inside the transaction (write to Kafka before commit).** Rejected:
  Kafka is not transactional with PostgreSQL; the event could be published and
  then the transaction rolled back, which is the opposite failure (consumers act
  on a deployment that never happened) and harder to detect.
- **Publish-then-retry in memory with a background queue.** Rejected: a process
  restart loses the queue, which is the same loss the outbox exists to prevent.
- **Kafka transactions (exactly-once semantics).** Rejected for now: they
  require a transactional producer configuration and still cannot include the
  PostgreSQL commit, so they narrow rather than close the window; they also add
  operational constraints (transaction timeouts, fencing) that a control plane
  with human-scale traffic does not need. The idempotent-consumer design already
  makes at-least-once safe.
- **Log-only failure (status quo).** Rejected: it preserves the data loss and
  makes the audit trail inconsistent with the deployment ledger.
