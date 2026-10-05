using Atlas.Modules.Audit.Domain;
using Atlas.Modules.Audit.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// Sorting is part of the list endpoints' HTTP contract: a whitelisted field
/// must actually reorder the query (before paging), and anything else must be
/// a 400 ProblemDetails that names the allowed values — never silently
/// unsorted data.
/// </summary>
public sealed class ListEndpointSortingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid OrganizationA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public ListEndpointSortingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    }

    [Fact]
    public async Task Audit_list_is_reordered_by_a_whitelisted_field_in_both_directions()
    {
        await SeedAsync("SortProbeAscending", "r-a", "r-c", "r-b");

        Assert.Equal(new[] { "r-a", "r-b", "r-c" }, await AuditResourceIdsAsync("SortProbeAscending", "resourceId", "asc"));
        Assert.Equal(new[] { "r-c", "r-b", "r-a" }, await AuditResourceIdsAsync("SortProbeAscending", "resourceId", "desc"));
    }

    [Fact]
    public async Task Sort_field_names_and_directions_are_case_insensitive()
    {
        await SeedAsync("SortProbeCase", "r-b", "r-a");

        Assert.Equal(new[] { "r-a", "r-b" }, await AuditResourceIdsAsync("SortProbeCase", "RESOURCEID", "ASC"));
    }

    [Fact]
    public async Task Unknown_sort_field_is_a_problem_details_400_naming_the_allowed_fields()
    {
        using var request = Request($"/api/v1/audit?organizationId={OrganizationA}&sortBy=severity");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_SORT_FIELD", body);
        Assert.Contains("createdAtUtc", body);
    }

    [Fact]
    public async Task Invalid_sort_direction_is_a_problem_details_400()
    {
        using var request = Request($"/api/v1/audit?organizationId={OrganizationA}&sortBy=action&sortDirection=sideways");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_SORT_DIRECTION", body);
    }

    [Fact]
    public async Task Unknown_sort_field_is_rejected_before_any_database_work()
    {
        // Validation lives in the controller, so even a resource the caller
        // cannot see is rejected without a query being issued.
        using var request = Request($"/api/v1/deployments?organizationId={OrganizationA}&sortBy=speed");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_SORT_FIELD", body);
        Assert.Contains("deployedAtUtc", body);
    }

    [Theory]
    [InlineData("/api/v1/apis?organizationId={0}&sortBy=name")]
    [InlineData("/api/v1/apis/routes?organizationId={0}&sortBy=path&sortDirection=desc")]
    [InlineData("/api/v1/services?organizationId={0}&sortBy=name")]
    [InlineData("/api/v1/deployments?organizationId={0}&sortBy=deployedAtUtc&sortDirection=desc")]
    [InlineData("/api/v1/events/dead-letters?sortBy=lastFailedAtUtc")]
    [InlineData("/api/v1/policies?organizationId={0}&sortBy=name")]
    [InlineData("/api/v1/incidents?organizationId={0}&sortBy=severity")]
    public async Task Whitelisted_fields_are_accepted_on_every_paginated_list_endpoint(string template)
    {
        using var request = Request(string.Format(template, OrganizationA));
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Organizations_list_accepts_a_whitelisted_sort_for_platform_admins()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/organizations?sortBy=slug&sortDirection=desc");
        request.Headers.Add("X-Test-Role", "PlatformAdmin");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SecurityEngineer");
        return request;
    }

    private async Task<string[]> AuditResourceIdsAsync(string resourceType, string sortBy, string sortDirection)
    {
        using var request = Request($"/api/v1/audit?organizationId={OrganizationA}&resourceType={resourceType}&sortBy={sortBy}&sortDirection={sortDirection}");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray().Select(entry => entry.GetProperty("resourceId").GetString()!).ToArray();
    }

    private async Task SeedAsync(string resourceType, params string[] resourceIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        foreach (var resourceId in resourceIds)
            db.Entries.Add(AuditEntry.Create(null, "sort-test", OrganizationA, "sort.probe", resourceType, resourceId, Guid.NewGuid()));
        await db.SaveChangesAsync();
    }
}
