using Atlas.Modules.Identity.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Atlas.Modules.Identity.Infrastructure;

public sealed class AtlasClaimsPrincipalFactory : UserClaimsPrincipalFactory<AtlasUser, AtlasRole>
{
    public AtlasClaimsPrincipalFactory(UserManager<AtlasUser> userManager, RoleManager<AtlasRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor) : base(userManager, roleManager, optionsAccessor) { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AtlasUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.CurrentOrganizationId is { } organizationId)
            identity.AddClaim(new Claim("org_id", organizationId.ToString()));
        identity.AddClaim(new Claim("atlas_user_active", user.IsActive.ToString()));
        return identity;
    }
}
