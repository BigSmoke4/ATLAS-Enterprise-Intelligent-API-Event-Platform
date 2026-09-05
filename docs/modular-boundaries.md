# Modular Boundaries

Each module directory follows the same shape:

    Modules/<Name>/
      Domain/          # entities, value objects, domain events — no EF, no ASP.NET
      Application/      # use cases, interfaces the Presentation layer calls
      Infrastructure/    # EF Core DbContext, Redis/Kafka adapters
      Presentation/      # <Name>Module : IAtlasModule — the only public entry point

`Atlas.Web` depends on every module's `Presentation` namespace only, via the
`IAtlasModule` interface defined in `Atlas.Shared`. It never adds a
`ProjectReference`-level dependency that would let it new-up a module's
internal `DbContext` or domain type directly.

Cross-module communication happens via:
1. Integration events published through `IEventPublisher` (Kafka-backed in
   production).
2. Read-only DTOs returned from one module's `Application` interfaces,
   consumed by another module's `Application` layer — never its `Domain`.
