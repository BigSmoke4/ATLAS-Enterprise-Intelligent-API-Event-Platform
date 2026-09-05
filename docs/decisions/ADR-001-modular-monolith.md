# ADR-001: Why Modular Monolith
**Status**: Accepted
**Context**: Need enterprise-grade module boundaries without the operational
cost of microservices for a system this early in its life.
**Decision**: One deployable, one database, strict module boundaries
enforced by `IAtlasModule` + architecture tests, so modules can be
extracted into services later if a real scaling need justifies it.
**Consequences**: Faster iteration and simpler ops now; some discipline
required to not let modules reach into each other's internals.
