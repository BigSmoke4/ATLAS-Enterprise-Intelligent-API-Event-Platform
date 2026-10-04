using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// Verifies the MVC (Razor) pages enforce the same tenant isolation as the
/// JSON APIs: an explicit organizationId must match the caller's org claim,
/// and org members land on their own data by default. PlatformAdmin spans
/// organizations by design.
/// </summary>
public sealed class MvcPageAuthorizationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly Guid OrganizationA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganizationB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly HttpClient _client;

    public MvcPageAuthorizationTests(TestWebApplicationFactory factory)
        => _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task Services_page_defaults_to_the_callers_organization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Services");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "Viewer");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Services_page_rejects_an_explicit_foreign_organization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Services?organizationId={OrganizationB}");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SRE");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Incidents_page_rejects_an_explicit_foreign_organization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Incidents?organizationId={OrganizationB}");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SRE");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Platform_admin_may_view_an_explicit_organization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Services?organizationId={OrganizationB}");
        request.Headers.Add("X-Test-Role", "PlatformAdmin");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ai_ask_is_rejected_for_a_foreign_tenant()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/ask");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SRE");
        request.Content = new StringContent(
            $"{{\"organizationId\":\"{OrganizationB}\",\"question\":\"status?\",\"toolNames\":[]}}",
            Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
