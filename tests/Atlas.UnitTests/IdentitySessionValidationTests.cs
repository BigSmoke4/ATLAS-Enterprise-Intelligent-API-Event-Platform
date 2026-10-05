using System.Security.Claims;
using Atlas.Modules.Identity.Domain;
using Atlas.Modules.Identity.Presentation;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The cookie-revalidation decision: a session survives only while the account
/// is active and the cookie's security stamp still matches the stored one.
/// Rotating the stamp (revoke-all) or clearing IsActive (deactivate) therefore
/// invalidates every live cookie on its next request.
/// </summary>
public class IdentitySessionValidationTests
{
    /// <summary>The framework's configured claim type, read from IdentityOptions rather than hardcoded.</summary>
    private static readonly string StampClaim = new IdentityOptions().ClaimsIdentity.SecurityStampClaimType;

    private static AtlasUser User(bool isActive = true, string? stamp = "stamp-1") => new()
    {
        Id = Guid.NewGuid(),
        UserName = "operator@atlas.local",
        DisplayName = "Operator",
        SecurityStamp = stamp,
        IsActive = isActive,
    };

    private static ClaimsPrincipal Cookie(string? stamp) =>
        new(new ClaimsIdentity(new[] { new Claim(StampClaim, stamp ?? string.Empty) }, "Identity.Application"));

    [Fact]
    public void A_cookie_whose_security_stamp_still_matches_is_accepted()
        => Assert.True(IdentitySessionValidation.IsStillValid(User(), Cookie("stamp-1"), StampClaim));

    [Fact]
    public void A_rotated_security_stamp_rejects_every_older_cookie()
    {
        var user = User(stamp: "stamp-2"); // rotated after the cookie below was issued
        Assert.False(IdentitySessionValidation.IsStillValid(user, Cookie("stamp-1"), StampClaim));
    }

    [Fact]
    public void A_deactivated_account_rejects_its_cookie_even_when_the_stamp_matches()
        => Assert.False(IdentitySessionValidation.IsStillValid(User(isActive: false), Cookie("stamp-1"), StampClaim));

    [Fact]
    public void A_cookie_without_a_stamp_claim_is_rejected()
        => Assert.False(IdentitySessionValidation.IsStillValid(User(), Cookie(null), StampClaim));

    [Fact]
    public void An_account_without_a_stored_stamp_cannot_vouch_for_a_cookie()
        => Assert.False(IdentitySessionValidation.IsStillValid(User(stamp: null), Cookie("stamp-1"), StampClaim));

    [Fact]
    public void The_stamp_comparison_is_ordinal_not_culture_sensitive()
        => Assert.False(IdentitySessionValidation.IsStillValid(User(stamp: "Stamp-1"), Cookie("stamp-1"), StampClaim));
}
