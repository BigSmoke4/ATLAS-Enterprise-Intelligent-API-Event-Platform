namespace Atlas.Shared.Contracts;

/// <summary>Read-only cross-module seam for request enforcement. The API catalog owns route configuration; Reliability owns enforcement.</summary>
public interface IRoutePolicyProvider
{
    Task<RoutePolicySnapshot?> FindAsync(Guid? organizationId, string path, string method, CancellationToken ct = default);
}

public sealed record RoutePolicySnapshot(int LimitPerWindow, TimeSpan Window, string Scope);
