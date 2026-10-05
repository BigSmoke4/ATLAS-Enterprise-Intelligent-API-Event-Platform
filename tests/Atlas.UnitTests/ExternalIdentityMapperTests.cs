using System.Security.Claims;
using Atlas.Modules.Identity.Domain;
using Atlas.Modules.Identity.Presentation;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The identity-resolution policy for federated sign-in, without a provider:
/// which claim is trusted, what happens when it is missing, and the rule that a
/// local account must already exist and be active.
/// </summary>
public class ExternalIdentityMapperTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    public void The_standard_email_claim_is_preferred()
    {
        var principal = Principal(("email", "operator@atlas.local"), ("preferred_username", "operator@legacy.local"));
        Assert.Equal("operator@atlas.local", ExternalIdentityMapper.EmailFrom(principal));
    }

    [Theory]
    [InlineData(ClaimTypes.Email)]
    [InlineData("preferred_username")]
    [InlineData("upn")]
    public void The_known_provider_conventions_are_accepted_in_order(string claimType)
    {
        var principal = Principal((claimType, "operator@atlas.local"));
        Assert.Equal("operator@atlas.local", ExternalIdentityMapper.EmailFrom(principal));
    }

    [Fact]
    public void A_provider_local_subject_is_not_an_account_identifier()
    {
        // "sub" identifies the user at the provider only; treating it as an
        // account address would let one provider's identifier collide with a
        // local account (or another provider's), so it is deliberately ignored.
        var principal = Principal(("sub", "auth0|5f8a3c1d9e"), ("name", "Operator"));
        Assert.Null(ExternalIdentityMapper.EmailFrom(principal));
    }

    [Fact]
    public void Blank_claim_values_are_skipped_and_values_are_trimmed()
    {
        var principal = Principal(("email", "   "), ("preferred_username", " operator@atlas.local "));
        Assert.Equal("operator@atlas.local", ExternalIdentityMapper.EmailFrom(principal));
        Assert.Null(ExternalIdentityMapper.EmailFrom(Principal(("email", "   "))));
        Assert.Null(ExternalIdentityMapper.EmailFrom(null));
    }

    [Fact]
    public void An_existing_active_account_is_usable()
        => Assert.True(ExternalIdentityMapper.IsUsable(new AtlasUser { IsActive = true }));

    [Fact]
    public void A_disabled_account_is_refused_and_an_unknown_identity_is_never_provisioned()
    {
        Assert.False(ExternalIdentityMapper.IsUsable(new AtlasUser { IsActive = false }));
        Assert.False(ExternalIdentityMapper.IsUsable(null));
    }
}
