# ADR-007: Idempotent consumers by database uniqueness, not by hope

**Status:** Accepted — implemented.

## Context
Kafka delivers at least once. Retries, rebalances, replays from the dead-letter
queue and consumer restarts all mean the same event can be handed to a handler
more than once, and a handler with a side effect (recording an audit entry,
changing state, calling an external system) must not run twice for one logical
event.

## Decision
Idempotency is enforced by the **database**, before the handler runs:

- `IdempotencyRecord` holds one row per `(ConsumerGroup, EventId)` with a unique
  index; the consumer-side coordinator attempts the insert and treats a
  uniqueness violation as "already processed" — no lookup-then-write race.
- The consumer group is part of the key, so two different consumers may each
  process the same event once (they are different logical subscribers), while a
  single consumer never processes it twice.
- Dead-letter replay goes through the same coordinator, so replaying an event
  that was actually processed is a no-op rather than a duplicate side effect.
- Handlers must be written as if they could be invoked twice; the test suite
  includes an integration test that publishes the same event twice and asserts
  the observable effect happened once.

## Consequences
- The guarantee is durable across process restarts and applies to every handler
  uniformly, because it lives in the consumer coordinator rather than in each
  handler.
- The idempotency table grows with consumed events and needs the same retention
  thinking as any other table (`docs/event-driven-architecture.md`).
- Business meaning is preserved: "exactly once" is delivered as
  *effectively-once*, which is the only thing a message broker can promise.

## Alternatives considered
- **In-memory dedupe set** — rejected: lost on restart and wrong across
  instances.
- **Handler-level checks ("if not already done, do it")** — rejected: it
  re-introduces the race it claims to remove and has to be repeated in every
  handler.
