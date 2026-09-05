namespace Atlas.Modules.Reliability.Application;

public record RateLimitDecision(bool Allowed, int LimitPerWindow, TimeSpan Window, long CurrentCount);

/// <summary>
/// The real, distributed-aware decision point HTTP middleware calls per
/// request. Backed by Atlas.Shared.Contracts.IRateLimitStore (Redis in
/// production), so the decision is correct across multiple ATLAS instances,
/// not just within one process — this is the actual "must remain correct
/// when multiple nodes process requests simultaneously" requirement.
/// </summary>
public interface IRequestRateLimiter
{
    Task<RateLimitDecision> CheckAsync(string scopeKey, int limitPerWindow, TimeSpan window, CancellationToken ct = default);
}
