# ADR-003: Kafka behind `IEventPublisher` / `IEventConsumer`

**Status:** Accepted — implemented.

## Context
ATLAS both produces and consumes domain events (deployments, incidents, policy
outcomes) and must keep working when the broker does not. Consumers must be able
to fail a message without losing it, and reprocessing must not duplicate a
business effect.

## Decision
Kafka is reached only through two contracts in `src/Shared/Contracts`:
`IEventPublisher` and `IEventConsumer`. No module references `Confluent.Kafka`
directly, so the broker is an infrastructure choice rather than an architectural
one.

- **Envelope:** events carry `EventId`, `EventType`, `Version`, `TimestampUtc`,
  `CorrelationId`, `CausationId`, `Producer`; the publisher writes them as Kafka
  message headers *and* inside the payload, so a consumer never has to trust
  only the header. `EventContractValidator` checks the payload before it is
  produced.
- **Consumer:** manual commit after the message is either processed or
  dead-lettered; exponential-backoff retries; a `DeadLetterEvent` row with the
  failure reason, attempt count and original payload; inspection and authorized
  replay (dry-run first) through `POST /api/v1/events/dead-letters/{id}/replay`.
- **Outage behaviour:** when no broker is configured the publisher seam is
  simply absent (nothing silently no-ops); a broker failure during publish is
  logged, counted and *rethrown* to the caller, and a deployment is still
  recorded — event publication never rolls back a recorded business fact.
- **Optionality:** the web host starts and serves without Kafka; the consumer
  hosted service tolerates an unreachable broker and never takes the process
  down with it.

## Consequences
- Consumers are testable without a broker, and an integration test asserts the
  header/payload round-trip against a real Kafka.
- At-least-once delivery is the contract; see ADR-007 for the idempotency that
  makes it safe.
- A transactional outbox for publish-under-outage is documented as remaining
  work (`docs/event-driven-architecture.md`) rather than pretended to exist.

## Alternatives considered
- **Direct Confluent.Kafka usage in modules** — rejected: it would leak a
  vendor into the domain and make the broker mandatory.
- **RabbitMQ/SQS** — rejected for this platform: the requirement is a
  partitioned, replayable log with consumer groups and lag visibility, which is
  what Kafka (and the consumer-lag surface) provides.
