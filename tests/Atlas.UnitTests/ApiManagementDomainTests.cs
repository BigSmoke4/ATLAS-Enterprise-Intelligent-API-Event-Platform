using Atlas.Modules.APIManagement.Domain;
using Xunit;

namespace Atlas.UnitTests;

public sealed class ApiManagementDomainTests
{
    [Fact]
    public void Route_normalizes_http_method_and_rejects_invalid_paths()
    {
        var api = ApiDefinition.Create(Guid.NewGuid(), "Orders", "/orders");
        var version = api.AddVersion(1);
        var route = version.AddRoute("/orders", "get");

        Assert.Equal("GET", route.HttpMethod);
        Assert.Throws<ArgumentException>(() => version.AddRoute("orders", "GET"));
    }

    [Fact]
    public void Route_policy_rejects_invalid_values()
    {
        var api = ApiDefinition.Create(Guid.NewGuid(), "Orders", "/orders");
        var route = api.AddVersion(1).AddRoute("/orders", "GET");

        Assert.Throws<ArgumentException>(() => route.SetRateLimit(new RateLimitPolicy(0, TimeSpan.FromMinutes(1), RateLimitScope.Ip)));
        Assert.Throws<ArgumentOutOfRangeException>(() => route.SetRetryPolicy(11));
    }
}
