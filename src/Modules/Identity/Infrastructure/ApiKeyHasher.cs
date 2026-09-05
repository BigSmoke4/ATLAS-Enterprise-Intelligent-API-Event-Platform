using System.Security.Cryptography;
using System.Text;

namespace Atlas.Modules.Identity.Infrastructure;

/// <summary>SHA-256 hashing for API keys. The raw key is shown to the user exactly once, at creation time.</summary>
public static class ApiKeyHasher
{
    public static (string RawKey, string Prefix, string Hash) GenerateNew()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = "atl_" + Convert.ToHexString(bytes).ToLowerInvariant();
        var prefix = raw[..12];
        var hash = Hash(raw);
        return (raw, prefix, hash);
    }

    public static string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
