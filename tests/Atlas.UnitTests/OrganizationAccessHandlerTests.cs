using System.Security.Claims;
using Atlas.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Atlas.UnitTests;

file class FakeHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}

public class OrganizationAccessHandlerTests
{
    private static HttpContext BuildContext(Guid? queryOrgId, ClaimsPrincipal user)
    {
        var context = new DefaultHttpContext();
        context.User = user;
        if (queryOrgId.HasValue)
        {
            context.Request.QueryString = new QueryString($"?organizationId={queryOrgId.Value}");
            context.Request.Query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["organizationId"] = queryOrgId.Value.ToString()
            });
        }
        return context;
    }

    private static ClaimsPrincipal BuildUser(Guid? orgId, string role = "SRE")
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (orgId.HasValue) claims.Add(new Claim("org_id", orgId.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static async Task<bool> Evaluate(HttpContext httpContext, ClaimsPrincipal user)
    {
        var accessor = new FakeHttpContextAccessor { HttpContext = httpContext };
        var handler = new OrganizationAccessHandler(accessor);
        var requirement = new OrganizationAccessRequirement();
        var authContext = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);

        await handler.HandleAsync(authContext);
        return authContext.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_when_requested_org_matches_callers_org_claim()
    {
        var orgId = Guid.NewGuid();
        var user = BuildUser(orgId);
        var context = BuildContext(orgId, user);

        Assert.True(await Evaluate(context, user));
    }

    [Fact]
    public async Task Fails_when_requested_org_does_not_match_callers_org_claim()
    {
        var callerOrg = Guid.NewGuid();
        var requestedOrg = Guid.NewGuid();
        var user = BuildUser(callerOrg);
        var context = BuildContext(requestedOrg, user);

        Assert.False(await Evaluate(context, user));
    }

    [Fact]
    public async Task PlatformAdmin_bypasses_the_check_even_across_organizations()
    {
        var callerOrg = Guid.NewGuid();
        var requestedOrg = Guid.NewGuid();
        var user = BuildUser(callerOrg, role: "PlatformAdmin");
        var context = BuildContext(requestedOrg, user);

        Assert.True(await Evaluate(context, user));
    }

    [Fact]
    public async Task Succeeds_when_request_has_no_organizationId_to_check()
    {
        var user = BuildUser(Guid.NewGuid());
        var context = BuildContext(null, user);

        Assert.True(await Evaluate(context, user));
    }

    [Fact]
    public async Task Fails_when_caller_has_no_org_claim_but_request_specifies_one()
    {
        var user = BuildUser(null);
        var context = BuildContext(Guid.NewGuid(), user);

        Assert.False(await Evaluate(context, user));
    }
}
