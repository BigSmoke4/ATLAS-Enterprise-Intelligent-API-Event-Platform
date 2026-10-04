using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atlas.IntegrationTests;

public sealed class AuthenticatedAuthorizationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static readonly Guid OrganizationA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganizationB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public AuthenticatedAuthorizationTests(TestWebApplicationFactory factory)
    {
        // BaseAddress must be HTTPS: Program.cs deliberately answers 401 (not
        // an HTTPS redirect) to plain-HTTP /api requests that carry no API
        // key, which would mask the authorization decision these tests assert.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    }

    [Fact]
    public async Task Authenticated_user_cannot_read_another_organization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/organizations/{OrganizationB}");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "Viewer");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Developer_cannot_execute_platform_admin_policy_operation()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/policies/{Guid.NewGuid()}/deactivate?organizationId={OrganizationA}");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "Developer");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Security_engineer_cannot_read_another_organizations_audit_trail()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/audit?organizationId={OrganizationB}");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SecurityEngineer");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SRE_cannot_use_a_different_organization_in_a_body_operation()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/traffic/select-instance");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SRE");
        request.Content = new StringContent($"{{\"organizationId\":\"{OrganizationB}\",\"serviceId\":\"{Guid.NewGuid()}\",\"strategy\":0}}", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SRE_cannot_report_instance_telemetry_for_a_foreign_organization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/traffic/telemetry");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SRE");
        request.Content = new StringContent(
            $"{{\"organizationId\":\"{OrganizationB}\",\"serviceId\":\"{Guid.NewGuid()}\",\"instances\":[{{\"instanceId\":\"{Guid.NewGuid()}\",\"activeConnections\":3,\"avgLatencyMs\":12.5}}]}}",
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SRE_can_report_instance_telemetry_for_their_own_organization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/traffic/telemetry");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SRE");
        request.Content = new StringContent(
            $"{{\"organizationId\":\"{OrganizationA}\",\"serviceId\":\"{Guid.NewGuid()}\",\"instances\":[{{\"instanceId\":\"{Guid.NewGuid()}\",\"activeConnections\":3,\"avgLatencyMs\":12.5}}]}}",
            System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
