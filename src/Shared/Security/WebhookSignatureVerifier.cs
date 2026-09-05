using System.Security.Cryptography;
using System.Text;

namespace Atlas.Shared.Security;

/// <summary>
/// Real HMAC-SHA256 webhook signature verification (GitHub/Stripe-style:
/// signature = hex(HMAC-SHA256(secret, rawBody))), with constant-time
/// comparison to avoid a timing side-channel. This is the mechanism piece
/// for the threat-model's "webhook spoofing" mitigation — no concrete
/// webhook RECEIVER endpoint exists yet in any module, since none of them
/// currently accept third-party webhooks, but any future one (e.g. a CI/CD
/// deployment-notification receiver for DeploymentIntelligence) should
/// verify through this before trusting the payload.
/// </summary>
public static class WebhookSignatureVerifier
{
    public static string ComputeSignature(string secret, byte[] rawBody)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(rawBody);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <returns>true only if providedSignatureHex matches the HMAC of rawBody under secret, compared in constant time.</returns>
    public static bool Verify(string secret, byte[] rawBody, string providedSignatureHex)
    {
        if (string.IsNullOrWhiteSpace(providedSignatureHex)) return false;

        var expected = ComputeSignature(secret, rawBody);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(providedSignatureHex.Trim().ToLowerInvariant());

        if (expectedBytes.Length != providedBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
