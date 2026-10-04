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

- **Retry *topics*** (as opposed to in-process retry with backoff). The current
  loop delays and retries the same consumer poll rather than republishing to a
  separate `topic.retry` topic, which is the more Kafka-idiomatic approach when
  a handler wants to release the partition during backoff. The in-process delay
  is bounded and the outcome is the same (retry, then DLQ), so this is an
  operational refinement rather than a correctness gap.
- **Consumer-side contract validation before dispatch.** The publisher validates
  every payload it produces (`EventContractValidator.ValidateJson`), so
  platform-emitted events are well formed at the source; an event produced by
  another system can still reach a handler and throw, which correctly routes it
  to the dead-letter queue. Validating against a schema registry *before*
  dispatch — and rejecting unknown versions there rather than in the handler — is
  the remaining step.
- **Transactional outbox.** Publication happens after the business transaction
  commits, with the failure logged and counted; a broker outage therefore loses
  the event rather than the business fact. An outbox table with a relay would
  make publication recoverable, and is the documented price of not writing
  Kafka into the request path today.
