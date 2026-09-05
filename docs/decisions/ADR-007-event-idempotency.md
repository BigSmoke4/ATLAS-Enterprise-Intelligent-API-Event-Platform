# ADR-007: Event Idempotency Strategy (planned)
Target approach: persist a processed-EventId ledger per consumer group
(unique constraint on (ConsumerGroup, EventId)) and check-then-skip before
executing the business operation. Not yet implemented — EventPlatform is a
planned module.
