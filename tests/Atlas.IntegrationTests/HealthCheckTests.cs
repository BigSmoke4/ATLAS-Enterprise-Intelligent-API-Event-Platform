using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// PLANNED: this currently only proves the host boots and /health responds.
/// Requires Postgres/Redis/Kafka reachable (see docker-compose.yml) — it
/// will fail fast without them, which is correct: readiness checks real
/// dependencies rather than always returning 200.
/// </summary>
public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public HealthCheckTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Liveness_endpoint_returns_success()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.True(response.IsSuccessStatusCode);
    }
}
