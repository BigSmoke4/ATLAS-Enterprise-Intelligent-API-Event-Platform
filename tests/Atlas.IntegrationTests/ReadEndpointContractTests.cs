using Atlas.Modules.Audit.Domain;
using Atlas.Modules.Audit.Infrastructure;
using Atlas.Modules.EventPlatform.Domain;
using Atlas.Modules.EventPlatform.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// The read endpoints publish Application DTOs, not EF aggregates. "No
/// rowVersion, no domain-event collector" is only provable on the wire, so
/// these tests read the real JSON through the real pipeline — and, because a
/// write to a mutable aggregate is the other half of the concurrency change,
/// one of them performs a real update (Detect → Transition) on PostgreSQL.
/// </summary>
public sealed class ReadEndpointContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly Guid OrganizationA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public ReadEndpointContractTests(TestWebApplicationFactory factory) => _factory = factory;

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    private static HttpRequestMessage Request(HttpMethod method, string url, string role, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-Organization", OrganizationA.ToString());
        request.Headers.Add("X-Test-Role", role);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<Guid> DeclareIncidentAsync(HttpClient client, string title)
    {
        using var declare = Request(HttpMethod.Post, "/api/v1/incidents", "SRE", new
        {
            organizationId = OrganizationA,
            title,
            severity = "Sev2",
            startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-12),
            affectedServiceIds = new[] { Guid.NewGuid() }
        });
        var response = await client.SendAsync(declare);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IncidentIdResponse>();
        Assert.NotNull(body);
        return body!.IncidentId;
    }

    private sealed record IncidentIdResponse(Guid IncidentId);

    [Fact]
    public async Task Incident_reads_return_dtos_and_a_real_update_still_succeeds_with_the_concurrency_token()
    {
        using var client = Client();
        var incidentId = await DeclareIncidentAsync(client, $"contract probe {Guid.NewGuid():N}");

        // A second request updates the row the first request inserted. If the
        // aggregate's token were misconfigured (e.g. a column that is never
        // populated) this SaveChanges would raise DbUpdateConcurrencyException.
        using var transition = Request(HttpMethod.Post, $"/api/v1/incidents/{incidentId}/transition", "SRE", new
        {
            organizationId = OrganizationA,
            target = "Investigating",
            note = "contract probe acknowledged"
        });
        var transitionResponse = await client.SendAsync(transition);
        Assert.Equal(HttpStatusCode.NoContent, transitionResponse.StatusCode);

        using var list = Request(HttpMethod.Get, $"/api/v1/incidents?organizationId={OrganizationA}&pageSize=200", "SecurityEngineer");
        var listResponse = await client.SendAsync(list);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(incidentId.ToString(), listBody);
        Assert.Contains("meanTimeToDetect", listBody);
        Assert.Contains("affectedServiceIds", listBody);
        Assert.Contains("timeline", listBody);
        Assert.DoesNotContain("rowVersion", listBody);
        Assert.DoesNotContain("domainEvents", listBody);

        using var detail = Request(HttpMethod.Get, $"/api/v1/incidents/{incidentId}?organizationId={OrganizationA}", "SecurityEngineer");
        var detailBody = await (await client.SendAsync(detail)).Content.ReadAsStringAsync();
        Assert.Contains("Investigating", detailBody);
        Assert.Contains("contract probe acknowledged", detailBody);
        Assert.DoesNotContain("domainEvents", detailBody);
    }

    [Fact]
    public async Task Deployment_list_returns_the_dto_shape()
    {
        using var client = Client();
        var version = $"2.0.{Guid.NewGuid().ToString("N")[..6]}";
        using var record = Request(HttpMethod.Post, "/api/v1/deployments", "SRE", new
        {
            organizationId = OrganizationA,
            serviceId = Guid.NewGuid(),
            version,
            environment = "staging",
            commitSha = Guid.NewGuid().ToString("N")[..40],
            author = "contract-probe"
        });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(record)).StatusCode);

        using var list = Request(HttpMethod.Get, $"/api/v1/deployments?organizationId={OrganizationA}&pageSize=200", "SecurityEngineer");
        var response = await client.SendAsync(list);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(version, body);
        Assert.Contains("commitSha", body);
        Assert.Contains("deployedAtUtc", body);
        Assert.DoesNotContain("rowVersion", body);
        Assert.DoesNotContain("domainEvents", body);
    }

    [Fact]
    public async Task Policy_list_returns_the_dto_shape()
    {
        using var client = Client();
        var name = $"contract-policy-{Guid.NewGuid():N}";
        using var create = Request(HttpMethod.Post, "/api/v1/policies", "PlatformAdmin", new
        {
            organizationId = OrganizationA,
            name,
            conditions = new[] { new { fieldName = "errorRate", @operator = "GreaterThan", value = 0.05 } },
            action = new { type = "RaiseAlert", alertSeverity = "Sev3" }
        });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create)).StatusCode);

        using var list = Request(HttpMethod.Get, $"/api/v1/policies?organizationId={OrganizationA}&pageSize=200", "SecurityEngineer");
        var response = await client.SendAsync(list);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(name, body);
        Assert.Contains("conditions", body);
        Assert.Contains("version", body);
        Assert.DoesNotContain("rowVersion", body);
        Assert.DoesNotContain("domainEvents", body);
    }

    [Fact]
    public async Task Dead_letter_list_returns_the_dto_shape()
    {
        var eventType = $"ContractProbe{Guid.NewGuid().ToString("N")[..8]}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventPlatformDbContext>();
            db.DeadLetterEvents.Add(DeadLetterEvent.Create("atlas.contract-probe", Guid.NewGuid(), eventType, Guid.NewGuid(), "{\"probe\":true}", "contract probe failure"));
            await db.SaveChangesAsync();
        }

        using var client = Client();
        using var list = Request(HttpMethod.Get, "/api/v1/events/dead-letters?pageSize=200", "SecurityEngineer");
        var response = await client.SendAsync(list);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(eventType, body);
        Assert.Contains("payloadJson", body);
        Assert.Contains("failureReason", body);
        Assert.DoesNotContain("rowVersion", body);
        Assert.DoesNotContain("domainEvents", body);
    }

    [Fact]
    public async Task Audit_list_returns_the_dto_shape()
    {
        var resourceId = $"contract-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            db.Entries.Add(AuditEntry.Create(null, "contract-probe", OrganizationA, "contract.probe", "ReadEndpointContract", resourceId, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        using var client = Client();
        using var list = Request(HttpMethod.Get, $"/api/v1/audit?organizationId={OrganizationA}&pageSize=200", "SecurityEngineer");
        var response = await client.SendAsync(list);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(resourceId, body);
        Assert.Contains("actorDisplay", body);
        Assert.Contains("correlationId", body);
        Assert.DoesNotContain("rowVersion", body);
        Assert.DoesNotContain("domainEvents", body);
    }
}
