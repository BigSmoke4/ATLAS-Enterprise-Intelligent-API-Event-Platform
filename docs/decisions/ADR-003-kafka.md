# ADR-003: Why Kafka
Durable, replayable log semantics are required for event replay, DLQ
inspection, and consumer-group scaling — a simple queue (e.g. only Redis
lists) doesn't give replay-by-offset or multiple independent consumer
groups over the same stream.
