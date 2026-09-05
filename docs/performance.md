# Performance

No benchmark numbers are published here — none have been run in this
environment (no .NET SDK / network access to install one). Suggested
approach once you can build: k6 or `dotnet-counters`/`bombardier` against
`/health`, then real endpoints as they're implemented; record actual P50/
P95/P99 rather than the prompt's example figures, which were illustrative
only.
