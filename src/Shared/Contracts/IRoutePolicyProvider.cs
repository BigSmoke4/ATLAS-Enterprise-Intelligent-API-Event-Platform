namespace Atlas.Shared.Contracts;

/// <summary>
/// Read-only cross-module seam for request enforcement and telemetry
/// attribution. The API catalog owns route configuration; Reliability owns
/// rate-limit enforcement and Observability owns telemetry — neither may
/// reference APIManagement's DbContext, so every field they need travels
/// through this snapshot.
/// </summary>
public interface IRoutePolicyProvider
{
    /// <summary>
    /// Resolves the configured route for a request, or null when the path is
    /// not part of the organization's API catalog.
    /// </summary>
    Task<RoutePolicySnapshot?> FindAsync(Guid? organizationId, string path, string method, CancellationToken ct = default);
}

/// <summary>
/// A resolved route. Rate-limit fields are nullable because a route may exist
/// without a rate-limit policy configured (enforcement then applies ATLAS's
/// documented default) — the route still carries identity, service
/// attribution and timeout/retry configuration.
/// </summary>
public sealed record RoutePolicySnapshot(
    int? LimitPerWindow,
    TimeSpan? Window,
    string? Scope,
    string? Algorithm,
    Guid? ServiceId = null,
    Guid? ApiDefinitionId = null,
    Guid? ApiVersionId = null,
    TimeSpan? Timeout = null,
    int? MaxRetries = null);
