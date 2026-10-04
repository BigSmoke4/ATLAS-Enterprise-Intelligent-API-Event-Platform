using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// Renders every operations-console page through the real MVC pipeline as an
/// authenticated operator.
///
/// Razor views are compiled at build time, but they can still fail at render
/// time (a null model member, a partial with the wrong model, a tag helper
/// mismatch, a read model that throws on an empty database). These tests are
/// the regression guard for that class of defect, and they assert the page
/// carries its own console marker rather than silently degrading into an
/// error page that happens to return 200.
/// </summary>
public sealed class ConsolePageRenderTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Organization = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    private readonly HttpClient _client;

    public ConsolePageRenderTests(TestWebApplicationFactory factory)
        => _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    /// <summary>Every console route with the marker the layout must have rendered.</summary>
    public static TheoryData<string, string> ConsolePages => new()
    {
        { "/Dashboard", "data-page=\"dashboard\"" },
        { "/Services", "data-page=\"services\"" },
        { "/Apis", "data-page=\"apis\"" },
        { "/Incidents", "data-page=\"incidents\"" },
        { "/Observability", "data-page=\"observability\"" },
        { "/Events", "data-page=\"events\"" },
        { "/Deployments", "data-page=\"deployments\"" },
        { "/Policies", "data-page=\"policies\"" },
        { "/Reliability", "data-page=\"reliability\"" },
        { "/AiOps", "data-page=\"ai\"" },
        { "/Audit", "data-page=\"audit\"" }
    };

    [Theory]
    [MemberData(nameof(ConsolePages))]
    public async Task Console_page_renders_for_an_authenticated_operator(string path, string marker)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Organization", Organization);
        request.Headers.Add("X-Test-Role", "PlatformAdmin");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(marker, html, StringComparison.Ordinal);
        // Every console page must be a real page, not an empty shell.
        Assert.Contains("<main", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Console_pages_are_not_anonymous()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Dashboard");

        var response = await _client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect or HttpStatusCode.Forbidden,
            $"Expected the page to reject an anonymous caller, got {(int)response.StatusCode}.");
    }
}
