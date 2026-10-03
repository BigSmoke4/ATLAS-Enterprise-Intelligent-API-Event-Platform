using Atlas.Modules.Audit.Domain;
using Atlas.Modules.Audit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace Atlas.IntegrationTests;

public sealed class AuditEndpointIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly Guid OrganizationA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrganizationB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public AuditEndpointIntegrationTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Security_engineer_reads_only_own_organization_and_action_filter()
    {
        await SeedAsync(
            AuditEntry.Create(null, "a", OrganizationA, "dead_letter.replay", "DeadLetterEvent", "a-1", Guid.NewGuid()),
            AuditEntry.Create(null, "a", OrganizationA, "other.action", "PolicyRule", "a-2", Guid.NewGuid()),
            AuditEntry.Create(null, "b", OrganizationB, "dead_letter.replay", "DeadLetterEvent", "b-1", Guid.NewGuid()));
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/audit?organizationId={OrganizationA}&action=dead_letter.replay");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SecurityEngineer");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("a-1", body);
        Assert.DoesNotContain("a-2", body);
        Assert.DoesNotContain("b-1", body);
    }

    [Fact]
    public async Task Platform_admin_can_read_unscoped_system_audit_records()
    {
        await SeedAsync(AuditEntry.Create(null, "system", null, "dead_letter.replay", "DeadLetterEvent", "system-1", Guid.NewGuid()));
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit?action=dead_letter.replay");
        request.Headers.Add("X-Test-Role", "PlatformAdmin");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("system-1", body);
    }

    [Fact]
    public async Task Organization_role_must_supply_an_organization_filter()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit");
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", "SecurityEngineer");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task SeedAsync(params AuditEntry[] entries)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        db.Entries.AddRange(entries);
        await db.SaveChangesAsync();
    }
}
