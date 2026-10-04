using Atlas.Modules.Identity.Domain;
using Atlas.Modules.Identity.Infrastructure;
using Atlas.Shared.Application;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Identity.Application;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly IdentityDbContext _db;
    public ApiKeyService(IdentityDbContext db) => _db = db;

    public async Task<Result<CreatedApiKeyDto>> CreateAsync(Guid organizationId, Guid createdByUserId, string name,
        DateTimeOffset? expiresAtUtc, CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty || createdByUserId == Guid.Empty || string.IsNullOrWhiteSpace(name))
            return Result.Failure<CreatedApiKeyDto>("Organization, creator, and key name are required.", "VALIDATION_ERROR");
        if (expiresAtUtc <= DateTimeOffset.UtcNow)
            return Result.Failure<CreatedApiKeyDto>("Expiration must be in the future.", "VALIDATION_ERROR");

        var generated = ApiKeyHasher.GenerateNew();
        var key = ApiKey.Create(organizationId, createdByUserId, name.Trim(), generated.Hash, generated.Prefix, expiresAtUtc);
        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync(ct);
        return Result.Success(new CreatedApiKeyDto(key.Id, generated.RawKey, generated.Prefix, key.Name));
    }

    public async Task<Result> RevokeAsync(Guid organizationId, Guid apiKeyId, CancellationToken ct = default)
    {
        var key = await _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKeyId && k.OrganizationId == organizationId, ct);
        if (key is null) return Result.Failure("API key was not found.", "NOT_FOUND");
        key.Revoke();
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ApiKeySummaryDto>> ListAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.ApiKeys.AsNoTracking()
            .Where(k => k.OrganizationId == organizationId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .Select(k => new ApiKeySummaryDto(k.Id, k.Name, k.Prefix, k.RevokedAtUtc != null, k.CreatedAtUtc, k.ExpiresAtUtc, k.LastUsedAtUtc))
            .ToListAsync(ct);

    public async Task<ApiKeyPrincipal?> AuthenticateAsync(string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        var hash = ApiKeyHasher.Hash(rawKey);
        var key = await _db.ApiKeys.SingleOrDefaultAsync(k => k.HashedKey == hash, ct);
        if (key is null || !key.IsValid) return null;
        key.RecordUsage();
        await _db.SaveChangesAsync(ct);
        return new ApiKeyPrincipal(key.Id, key.OrganizationId, key.CreatedByUserId);
    }
}
