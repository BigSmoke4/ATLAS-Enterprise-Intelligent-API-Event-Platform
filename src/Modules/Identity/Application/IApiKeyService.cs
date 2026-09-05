using Atlas.Shared.Application;

namespace Atlas.Modules.Identity.Application;

public interface IApiKeyService
{
    Task<Result<CreatedApiKeyDto>> CreateAsync(Guid organizationId, Guid createdByUserId, string name, DateTimeOffset? expiresAtUtc, CancellationToken ct = default);
    Task<Result> RevokeAsync(Guid organizationId, Guid apiKeyId, CancellationToken ct = default);
    Task<ApiKeyPrincipal?> AuthenticateAsync(string rawKey, CancellationToken ct = default);
}

public record CreatedApiKeyDto(Guid Id, string RawKey, string Prefix, string Name);
public record ApiKeyPrincipal(Guid ApiKeyId, Guid OrganizationId, Guid CreatedByUserId);
