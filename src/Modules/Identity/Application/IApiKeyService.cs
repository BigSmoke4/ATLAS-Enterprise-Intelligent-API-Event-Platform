using Atlas.Shared.Application;

namespace Atlas.Modules.Identity.Application;

public interface IApiKeyService
{
    Task<Result<CreatedApiKeyDto>> CreateAsync(Guid organizationId, Guid createdByUserId, string name, DateTimeOffset? expiresAtUtc, CancellationToken ct = default);
    Task<Result> RevokeAsync(Guid organizationId, Guid apiKeyId, CancellationToken ct = default);
    Task<ApiKeyPrincipal?> AuthenticateAsync(string rawKey, CancellationToken ct = default);

    /// <summary>
    /// Key metadata for the console. The hash is never projected — the raw key
    /// exists only in the create response and is never retrievable again.
    /// </summary>
    Task<IReadOnlyList<ApiKeySummaryDto>> ListAsync(Guid organizationId, CancellationToken ct = default);
}

public record CreatedApiKeyDto(Guid Id, string RawKey, string Prefix, string Name);
public record ApiKeySummaryDto(Guid Id, string Name, string Prefix, bool IsRevoked, DateTimeOffset CreatedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? LastUsedAtUtc);
public record ApiKeyPrincipal(Guid ApiKeyId, Guid OrganizationId, Guid CreatedByUserId);
