using Atlas.Modules.APIManagement.Infrastructure;
using Atlas.Modules.Audit.Infrastructure;
using Atlas.Modules.DeploymentIntelligence.Infrastructure;
using Atlas.Modules.EventPlatform.Infrastructure;
using Atlas.Modules.Identity.Infrastructure;
using Atlas.Modules.IncidentManagement.Infrastructure;
using Atlas.Modules.Observability.Infrastructure;
using Atlas.Modules.Organizations.Infrastructure;
using Atlas.Modules.PolicyEngine.Infrastructure;
using Atlas.Modules.ServiceRegistry.Infrastructure;
using Atlas.Modules.TrafficManagement.Infrastructure;
using Atlas.Shared.Contracts;
using Atlas.Shared.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Atlas.UnitTests;

file sealed class OpenTenant : ITenantContext
{
    public Guid? CurrentOrganizationId => null;
    public bool HasOrganization => false;
}

/// <summary>
/// Locks in the optimistic-concurrency configuration for every module model, so
/// a new mutable aggregate cannot quietly ship without a token (last write wins)
/// and an append-only table cannot quietly acquire one. The store-level mapping
/// to PostgreSQL's <c>xmin</c> system column is what the generated migration and
/// the Postgres integration suites prove; this test proves the model intent.
/// </summary>
public class ConcurrencyTokenConfigurationTests
{
    /// <summary>
    /// Rows that are append-only or immutable by design — they have no UPDATE
    /// path (the audit ledger even rejects Modified/Deleted at SaveChanges), so
    /// a token would only add noise to every statement.
    /// </summary>
    private static readonly Type[] ImmutableByDesign =
    {
        typeof(Atlas.Modules.Audit.Domain.AuditEntry),
        typeof(Atlas.Modules.IncidentManagement.Domain.IncidentTimelineEntry),
        typeof(Atlas.Modules.Observability.Domain.MetricSample),
        typeof(Atlas.Modules.EventPlatform.Domain.IdempotencyRecord),
        typeof(Atlas.Modules.ServiceRegistry.Domain.ServiceDependency),
    };

    /// <summary>
    /// Rows whose updates are coordinated by an explicit claim instead of a row
    /// version: the outbox relay leases a message with one conditional
    /// <c>UPDATE</c> over <c>NextAttemptAtUtc</c>, so a token would turn every
    /// claim into an optimistic race the relay had to retry (ADR-011).
    /// </summary>
    private static readonly Type[] LeaseClaimedByDesign =
    {
        typeof(Atlas.Modules.DeploymentIntelligence.Domain.OutboxMessage),
    };

    private static (string Name, DbContext Context)[] Contexts()
    {
        var tenant = new OpenTenant();
        return new (string, DbContext)[]
        {
            ("APIManagement", new ApiManagementDbContext(Options<ApiManagementDbContext>(), tenant)),
            ("Audit", new AuditDbContext(Options<AuditDbContext>())),
            ("DeploymentIntelligence", new DeploymentIntelligenceDbContext(Options<DeploymentIntelligenceDbContext>(), tenant)),
            ("EventPlatform", new EventPlatformDbContext(Options<EventPlatformDbContext>())),
            ("Identity", new IdentityDbContext(Options<IdentityDbContext>())),
            ("IncidentManagement", new IncidentManagementDbContext(Options<IncidentManagementDbContext>(), tenant)),
            ("Observability", new ObservabilityDbContext(Options<ObservabilityDbContext>(), tenant)),
            ("Organizations", new OrganizationsDbContext(Options<OrganizationsDbContext>(), tenant)),
            ("PolicyEngine", new PolicyEngineDbContext(Options<PolicyEngineDbContext>(), tenant)),
            ("ServiceRegistry", new ServiceRegistryDbContext(Options<ServiceRegistryDbContext>(), tenant)),
            ("TrafficManagement", new TrafficManagementDbContext(Options<TrafficManagementDbContext>(), tenant)),
        };
    }

    private static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase($"concurrency-model-{typeof(TContext).Name}")
            .Options;

    [Fact]
    public void Every_mutable_aggregate_has_a_concurrency_token_and_nothing_else_does()
    {
        var problems = new List<string>();
        var mutableWithToken = 0;

        foreach (var (module, context) in Contexts())
        {
            using (context)
            {
                foreach (var entityType in context.Model.GetEntityTypes())
                {
                    if (!typeof(Entity).IsAssignableFrom(entityType.ClrType)) continue;

                    var label = $"{module}/{entityType.ClrType.Name}";
                    var token = entityType.FindProperty(nameof(Entity.RowVersion));

                    if (ImmutableByDesign.Contains(entityType.ClrType) || LeaseClaimedByDesign.Contains(entityType.ClrType))
                    {
                        if (token is not null)
                            problems.Add($"{label}: a row updated without a version token must not map RowVersion");
                        continue;
                    }

                    if (token is null)
                    {
                        problems.Add($"{label}: mutable aggregate has no concurrency token");
                        continue;
                    }

                    mutableWithToken++;
                    if (!token.IsConcurrencyToken) problems.Add($"{label}: RowVersion is not a concurrency token");
                    if (token.ValueGenerated != ValueGenerated.OnAddOrUpdate)
                        problems.Add($"{label}: RowVersion is not database-generated (ValueGenerated={token.ValueGenerated})");
                }
            }
        }

        Assert.True(problems.Count == 0, "Concurrency-token configuration problems:\n - " + string.Join("\n - ", problems));
        // A drift guard on the guard: if this ever reads 0 the sweep has stopped
        // looking at the models rather than the models having lost their tokens
        // (17 mutable aggregates are configured today).
        Assert.True(mutableWithToken >= 15, $"Expected the sweep to see the platform's mutable aggregates, saw {mutableWithToken}.");
    }
}
