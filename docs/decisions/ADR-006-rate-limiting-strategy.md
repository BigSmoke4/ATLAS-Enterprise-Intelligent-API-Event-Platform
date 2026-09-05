# ADR-006: Rate Limiting Strategy
Token bucket for smooth, bursty-but-bounded traffic (per API key/tenant);
sliding-window log where exact enforcement at a boundary matters more than
raw throughput. Both algorithms are implemented as pure, unit-tested
functions in `Modules/Reliability/Domain` so the Redis-backed distributed
store (planned) can be tested against the same math as an in-memory
single-node version.
