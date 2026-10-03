using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atlas.IntegrationTests;

public sealed class ReadinessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public ReadinessTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Readiness_endpoint_is_dependency_aware()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        // CI supplies PostgreSQL, Redis, and Kafka. A failing status is useful
        // evidence that a dependency is unavailable; the body identifies the
        // failing check rather than masking it with a hard-coded 200.
        Assert.True(response.IsSuccessStatusCode, body);
    }
}
