# Event-Driven Architecture

`Atlas.Shared.Contracts.IEventPublisher` / `IEventConsumer<T>` /
`IIntegrationEvent` define the shape: every event carries EventId,
EventType, Version, TimestampUtc, CorrelationId, CausationId, Producer.

## Implemented

- `Modules/EventPlatform/Infrastructure/KafkaEventPublisher` — a real
  Confluent.Kafka-backed `IEventPublisher` (idempotent producer, `Acks.All`,
  bounded retries with backoff). Registered only if `Kafka:BootstrapServers`
  is configured, so a missing config fails fast at DI resolution instead of
  silently no-op publishing.
- `Modules/EventPlatform/Application/IdempotencyGuard` — the actual
  duplicate-delivery defense (ADR-007): a unique DB index on
  `(ConsumerGroup, EventId)`. `TryMarkProcessedAsync` returns `false` both
  when the record already exists and when a concurrent insert loses the
  race, so it's safe under multiple consumer instances.
- `DeadLetterEvent` entity — the shape a DLQ row will take.

## Consumer side (now implemented)

- `KafkaEventConsumer` (`BackgroundService`) subscribes to `Kafka:Topics`,
  checks `IIdempotencyGuard` before dispatch (so a redelivered message is
  acknowledged without re-running business logic), dispatches via
  `IEventHandlerDispatcher`/`IEventHandlerRegistry` (modules register a
  handler per event-type string — the consumer never hard-codes event
  types), retries with exponential backoff up to `KafkaConsumerOptions.MaxRetries`,
  and on final failure calls `IDeadLetterService.RouteToDeadLetterAsync`,
  which increments `RetryCount` on an existing DLQ row instead of creating
  duplicates.
- Manual offset commit: a message's offset is only committed after it's
  either handled successfully or dead-lettered — never on a bare consume.
- `ConsumeException` (broker unreachable, etc.) is caught per poll
  iteration so a Kafka outage backs off and retries the loop rather than
  crashing the host.

## Replay (now implemented)

`IDeadLetterService.ReplayAsync(id, dryRun)` — dry-run validates
replayability (checks an `IEventPublisher` is actually configured) without
any side effect; live replay re-publishes the dead-lettered payload back
onto its **original topic** via `IEventPublisher` and only marks the row
replayed if the publish succeeds. Exposed via
`POST /api/v1/events/dead-letters/{id}/replay?dryRun=true|false`, restricted
to `Role:PlatformAdmin`. Unit-tested (dry-run never publishes, live replay
publishes exactly once, replaying twice is rejected) against EF Core's
InMemory provider.

`IDeadLetterService.MarkReplayedAsync` still exists separately for the
"fixed out-of-band, don't resend" case.

## Transactional outbox (now implemented)

Publication no longer happens *after* the business transaction commits, where a
broker outage lost the event while the business fact survived. The deployment
event is now written into an outbox row by the **same `SaveChanges` as the
deployment** (`DeploymentIntelligence`, table
`deploymentintelligence."OutboxMessages"`, migration
`20261005120000_OutboxMessages`), and `OutboxRelayService` — a hosted service —
publishes what is pending.

- **Atomicity.** `DeploymentRegressionService.RecordDeploymentAsync` adds the
  `Deployment` and its `OutboxMessage` in one transaction: if the deployment is
  durable the event is too, and if the transaction rolls back neither exists.
- **Delivery is at least once.** A row is marked sent only *after* the broker
  accepted the publish, so a crash in between republishes on the next pass. That
  is exactly why consumers are idempotent by construction (the unique
  `(ConsumerGroup, EventId)` index above): the two halves of the contract are
  designed together, and the duplicate is absorbed rather than prevented.
- **Claiming is a database operation, not a lock.** A pass selects pending rows
  that are due, then claims each with a conditional `UPDATE` that pushes
  `NextAttemptAtUtc` into the future (the lease). Two relay instances, or a
  restart, cannot publish the same row inside that window; a row whose publisher
  died becomes claimable again when the lease expires.
- **Failure handling.** Attempts are counted, the error is stored (truncated to
  the column width), and the retry is scheduled with exponential backoff
  (`Outbox:InitialBackoff` 5 s, doubling, capped by `Outbox:MaxBackoff` at
  10 min). After `Outbox:MaxAttempts` (default 10) the row is marked abandoned:
  it stays in the table for inspection and is never retried automatically. A
  pass that throws (database unreachable, say) is caught and logged — it never
  takes the host down.
- **Observability.** `atlas.events.published` carries the topic, the event type
  and `path="outbox"`; `atlas.outbox.abandoned` counts messages that exhausted
  their budget. A growing backlog with a flat publish rate is the signal that the
  broker is unreachable while the platform keeps serving.
- **Configuration** (`appsettings.json`, `Outbox` section): `PollInterval`,
  `BatchSize`, `MaxAttempts`, `LeaseDuration`, `InitialBackoff`, `MaxBackoff`.
  The relay is not started in the `Testing` environment, where the integration
  suites drive `RunOnceAsync` directly and must not race a background poller.
- **Verified.** `OutboxMessageTests` (unit: envelope validation at write time,
  the backoff schedule, abandonment after the last attempt, error truncation)
  and `OutboxRelayIntegrationTests` (real PostgreSQL: published once and marked
  sent, a failed publish stays pending with a future retry window, an abandoned
  row is never retried, a leased row is invisible to a second pass).

The deployment event is the only publisher that uses the outbox today; every
module that starts publishing an integration event copies this pattern instead of
publishing inside a request. With no broker configured the publisher seam is
absent and the relay logs once, at startup, that recorded events stay pending —
it never pretends to deliver them.

## Deliberately not implemented

- **Retry *topics*** (as opposed to in-process retry with backoff). The consumer
  loop delays and retries the same poll rather than republishing to a separate
  `topic.retry` topic, which is the more Kafka-idiomatic shape when a handler
  wants to release the partition during backoff. The in-process delay is bounded
  (`KafkaConsumerOptions.MaxRetries`, doubling to a one-minute cap) and the
  outcome is identical — retry, then dead-letter — so this stays an operational
  refinement, not a correctness gap.
- **Schema-registry-style version gating before dispatch.** Every consumed
  payload *is* validated before dispatch: `EventProcessingCoordinator.ProcessAsync`
  calls `EventContractValidator.ValidateJson` ahead of the idempotency claim, so a
  non-object envelope or a missing/empty EventId, EventType, CorrelationId,
  Producer, Version or TimestampUtc fails that attempt, exhausts the retries and
  lands in the dead-letter store (hostile-input tested by
  `EventContractValidatorTests`). What is not implemented is rejecting an
  *unknown event type or version* up front against a schema registry; today the
  registered handler validates the body it needs and throws, which routes the
  event to the dead-letter queue with a named failure reason. A registry — or a
  version gate in the coordinator — remains the next step for multi-producer
  deployments.
