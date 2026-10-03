using Atlas.Modules.Identity.Domain;
using Atlas.Modules.Identity.Infrastructure;
using Xunit;

namespace Atlas.UnitTests;

public sealed class ApiKeyTests
{
    [Fact]
    public void Generated_key_is_hashable_without_persisting_the_raw_secret()
    {
        var generated = ApiKeyHasher.GenerateNew();
        Assert.StartsWith("atl_", generated.RawKey);
        Assert.Equal(generated.Hash, ApiKeyHasher.Hash(generated.RawKey));
        Assert.NotEqual(generated.RawKey, generated.Hash);
        Assert.StartsWith(generated.Prefix, generated.RawKey);
    }

    [Fact]
    public void Revoked_key_is_invalid()
    {
        var key = ApiKey.Create(Guid.NewGuid(), Guid.NewGuid(), "ci", "hash", "atl_prefix", null);
        Assert.True(key.IsValid);
        key.Revoke();
        Assert.False(key.IsValid);
    }
}
