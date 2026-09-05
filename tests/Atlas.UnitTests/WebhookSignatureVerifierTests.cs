using System.Text;
using Atlas.Shared.Security;
using Xunit;

namespace Atlas.UnitTests;

public class WebhookSignatureVerifierTests
{
    [Fact]
    public void Verify_succeeds_with_correct_signature()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"deployment.completed\"}");
        var signature = WebhookSignatureVerifier.ComputeSignature("shared-secret", body);

        Assert.True(WebhookSignatureVerifier.Verify("shared-secret", body, signature));
    }

    [Fact]
    public void Verify_fails_with_wrong_secret()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"x\"}");
        var signature = WebhookSignatureVerifier.ComputeSignature("secret-a", body);

        Assert.False(WebhookSignatureVerifier.Verify("secret-b", body, signature));
    }

    [Fact]
    public void Verify_fails_if_body_is_tampered_with_after_signing()
    {
        var originalBody = Encoding.UTF8.GetBytes("{\"amount\":100}");
        var signature = WebhookSignatureVerifier.ComputeSignature("secret", originalBody);
        var tamperedBody = Encoding.UTF8.GetBytes("{\"amount\":100000}");

        Assert.False(WebhookSignatureVerifier.Verify("secret", tamperedBody, signature));
    }

    [Fact]
    public void Verify_fails_on_empty_or_malformed_signature()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        Assert.False(WebhookSignatureVerifier.Verify("secret", body, ""));
        Assert.False(WebhookSignatureVerifier.Verify("secret", body, "not-a-real-signature"));
    }
}
