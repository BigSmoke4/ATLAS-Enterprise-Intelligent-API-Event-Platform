using Atlas.Shared.Domain;

namespace Atlas.Modules.Identity.Domain;

/// <summary>
/// API keys are never stored raw. Only a SHA-256 hash and a short, non-secret
/// prefix (for lookup/display, e.g. "atl_ab12cd34ef56") are persisted.
/// </summary>
public class ApiKey : TenantEntity
{
    public string HashedKey { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    private ApiKey() { }

    public static ApiKey Create(Guid organizationId, Guid createdByUserId, string name, string hashedKey, string prefix, DateTimeOffset? expiresAtUtc)
    {
        return new ApiKey
        {
            OrganizationId = organizationId,
            CreatedByUserId = createdByUserId,
            Name = name,
            HashedKey = hashedKey,
            Prefix = prefix,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public bool IsValid => RevokedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > DateTimeOffset.UtcNow);

    public void Revoke() => RevokedAtUtc = DateTimeOffset.UtcNow;
    public void RecordUsage() => LastUsedAtUtc = DateTimeOffset.UtcNow;
}
