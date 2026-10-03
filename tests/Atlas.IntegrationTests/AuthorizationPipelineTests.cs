using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atlas.IntegrationTests;

public sealed class AuthorizationPipelineTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    public AuthorizationPipelineTests(TestWebApplicationFactory factory) => _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/api/v1/services?organizationId=11111111-1111-1111-1111-111111111111")]
    [InlineData("/api/v1/incidents?organizationId=11111111-1111-1111-1111-111111111111")]
    [InlineData("/api/v1/events/dead-letters")]
    public async Task Protected_api_resources_reject_anonymous_requests(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_is_available_without_authentication()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
