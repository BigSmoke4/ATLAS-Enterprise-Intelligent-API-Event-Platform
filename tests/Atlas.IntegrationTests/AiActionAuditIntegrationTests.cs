using Atlas.Modules.Audit.Domain;
using Atlas.Modules.Audit.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// Attribution for AI-driven actions. An action the assistant performs on the
/// platform is the highest-consequence write there is, so the audit entry must
/// name the human who confirmed it — not just a display name. The controller
/// reads the actor from <c>ClaimTypes.NameIdentifier</c>, which both the cookie
/// pipeline (Identity) and API-key middleware populate, so the same claim
/// resolves for every principal type.
///
/// The test asserts the ledger row rather than the action's outcome: a denied
/// or failed attempt is exactly as interesting to an auditor as a successful
/// one, and the audit write happens in both cases.
/// </summary>
public sealed class AiActionAuditIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>The user id the integration authentication handler puts in the NameIdentifier claim.</summary>
    private static readonly Guid TestActorUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AiActionAuditIntegrationTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task An_ai_action_attempt_is_attributed_to_the_authenticated_user_id()
    {
        var organizationId = Guid.NewGuid();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/actions");
        request.Headers.Add("X-Test-Organization", organizationId.ToString());
        request.Headers.Add("X-Test-Role", "PlatformAdmin");
        request.Content = JsonContent.Create(new
        {
            organizationId,
            toolName = "DeactivatePolicy",
            arguments = new Dictionary<string, string> { ["policyId"] = Guid.NewGuid().ToString() },
            explicitConfirmation = true
        });

        using var response = await client.SendAsync(request);

        // The policy referenced above does not exist, so the tool reports a
        // failure — which is the point: the attempt is what gets audited.
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity,
            $"Expected 200 or 422 from the action endpoint, got {(int)response.StatusCode} {response.StatusCode}.");

        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var entry = await audit.Entries.AsNoTracking()
            .Where(e => e.Action == "ai.action.DeactivatePolicy" && e.OrganizationId == organizationId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync();

        Assert.NotNull(entry);
        Assert.Equal(TestActorUserId, entry!.ActorUserId);
        Assert.Equal("integration-test", entry.ActorDisplay);
    }

    [Fact]
    public async Task An_unconfirmed_ai_action_is_denied_and_audited_with_the_same_attribution()
    {
        var organizationId = Guid.NewGuid();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/actions");
        request.Headers.Add("X-Test-Organization", organizationId.ToString());
        request.Headers.Add("X-Test-Role", "PlatformAdmin");
        request.Content = JsonContent.Create(new
        {
            organizationId,
            toolName = "DeactivatePolicy",
            arguments = new Dictionary<string, string>(),
            explicitConfirmation = false
        });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var entry = await audit.Entries.AsNoTracking()
            .Where(e => e.Action == "ai.action.DeactivatePolicy" && e.OrganizationId == organizationId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync();

        Assert.NotNull(entry);
        Assert.Equal(TestActorUserId, entry!.ActorUserId);
        Assert.Contains("confirm", entry.AfterJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
