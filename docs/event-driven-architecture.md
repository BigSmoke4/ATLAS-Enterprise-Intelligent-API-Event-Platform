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

## Not yet implemented

Retry *topics* (as opposed to in-process retry with backoff) — the current
retry loop delays and retries the same consumer poll rather than
republishing to a separate `topic.retry` topic, which is the more
Kafka-idiomatic approach if the initial handler needs to release the
partition during backoff. Also not implemented: payload schema validation
before dispatch (a malformed event can still reach a consumer's handler and
throw, which correctly routes it to DLQ, but there's no earlier
schema-level rejection).
