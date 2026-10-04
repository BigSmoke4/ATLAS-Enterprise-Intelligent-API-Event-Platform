using Atlas.Shared.Security;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The OIDC configuration contract: absent configuration means "local
/// authentication only" and is valid; partial configuration must fail loudly
/// rather than being ignored, because an operator who writes
/// <c>Oidc:ClientId</c> believes federation is enabled.
/// </summary>
public sealed class OidcOptionsTests
{
    [Fact]
    public void Absent_configuration_is_valid_and_means_local_auth_only()
    {
        var options = new OidcOptions();

        Assert.False(options.HasAnyValue);
        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Complete_configuration_is_valid()
    {
        var options = new OidcOptions
        {
            Authority = "https://login.example.com/tenant/v2.0",
            ClientId = "atlas-console",
            ClientSecret = "not-a-real-secret"
        };

        Assert.True(options.HasAnyValue);
        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData(null, "atlas-console")]
    [InlineData("https://login.example.com", null)]
    [InlineData("https://login.example.com/tenant", "")]
    public void Partial_configuration_is_rejected(string? authority, string? clientId)
    {
        var options = new OidcOptions { Authority = authority, ClientId = clientId };

        Assert.NotEmpty(options.Validate());
    }

    [Fact]
    public void A_client_secret_alone_is_already_a_statement_of_intent()
    {
        var options = new OidcOptions { ClientSecret = "not-a-real-secret" };

        Assert.True(options.HasAnyValue);
        Assert.Contains(options.Validate(), message => message.Contains("Oidc:Authority"));
    }

    [Fact]
    public void Authority_must_be_an_absolute_uri()
        => Assert.NotEmpty(new OidcOptions { Authority = "login.example.com", ClientId = "atlas" }.Validate());

    [Fact]
    public void Plain_http_authority_is_rejected_while_https_metadata_is_required()
    {
        var options = new OidcOptions { Authority = "http://login.example.com", ClientId = "atlas" };

        Assert.Contains(options.Validate(), message => message.Contains("https"));
    }

    [Fact]
    public void Plain_http_authority_is_allowed_only_when_https_metadata_is_explicitly_disabled()
    {
        var options = new OidcOptions
        {
            Authority = "http://localhost:8080",
            ClientId = "atlas",
            RequireHttpsMetadata = false
        };

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Scopes_must_include_openid_because_this_is_openid_connect()
    {
        var options = new OidcOptions
        {
            Authority = "https://login.example.com",
            ClientId = "atlas",
            Scopes = new[] { "profile", "email" }
        };

        Assert.Contains(options.Validate(), message => message.Contains("openid"));
    }
}
