using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.DeploymentIntelligence.Domain;
using Atlas.Modules.DeploymentIntelligence.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// The transactional outbox proven against real PostgreSQL.
///
/// The mechanism has two halves that only a database can demonstrate: the
/// message is written by the same <c>SaveChanges</c> as the business row, and
/// the relay claims it with a conditional <c>UPDATE</c> whose lease makes two
/// relay instances safe. The publisher is replaced by a recording double, so
/// these tests assert the relay's decisions (publish once, back off on failure,
/// abandon after the budget) without a broker — while the row state is read
/// back from the CI database, which is where at-least-once actually lives.
/// The CI pipeline applies the module migrations to `atlas_ci` before this
/// suite runs, so the outbox table is the real one.
/// </summary>
public sealed class OutboxRelayIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? "Host=localhost;Port=5432;Database=atlas_ci;Username=atlas;Password=atlas_ci";

    /// <summary>Headless host: the outbox is infrastructure state, not tenant data.</summary>
    private sealed class NoOrganization : ITenantContext
    {
        public Guid? CurrentOrganizationId => null;
        public bool HasOrganization => false;
    }

    private sealed record RawPublish(string Topic, string PayloadJson, string EventType, int Version,
        Guid EventId, Guid CorrelationId, Guid? CausationId, string Producer, DateTimeOffset TimestampUtc);

    private sealed class RecordingPublisher : IRawEventPublisher
    {
        private readonly bool _fail;
        public RecordingPublisher(bool fail = false) => _fail = fail;
        public List<RawPublish> Calls { get; } = new();

        public Task PublishRawAsync(string topic, string payloadJson, string eventType, int version, Guid eventId,
            Guid correlationId, Guid? causationId, string producer, DateTimeOffset timestampUtc, CancellationToken ct = default)
        {
            Calls.Add(new RawPublish(topic, payloadJson, eventType, version, eventId, correlationId, causationId, producer, timestampUtc));
            return _fail
                ? Task.FromException(new InvalidOperationException("broker unavailable"))
                : Task.CompletedTask;
        }
    }

    private static ServiceProvider Provider(RecordingPublisher publisher, Action<OutboxRelayOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext, NoOrganization>();
        services.AddSingleton<IRawEventPublisher>(publisher);
        services.AddDbContext<DeploymentIntelligenceDbContext>(options => options.UseNpgsql(ConnectionString));
        services.AddOptions<OutboxRelayOptions>().Configure(options => configure?.Invoke(options));
        return services.BuildServiceProvider();
    }

    private static OutboxRelayService Relay(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<OutboxRelayService>>(),
            provider.GetRequiredService<IOptions<OutboxRelayOptions>>());

    /// <summary>
    /// Writes one message exactly as the deployment service does: validated
    /// envelope, serialized payload, no publish call.
    /// </summary>
    private static async Task<Guid> EnqueueAsync(ServiceProvider provider)
    {
        var organizationId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        var @event = DeploymentRecordedIntegrationEvent.Create(
            organizationId, deploymentId, Guid.NewGuid(), "v1.0.0", "production", "abc1234", "integration-test");

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeploymentIntelligenceDbContext>();
        db.OutboxMessages.Add(OutboxMessage.Create(DeploymentTopics.Deployments, @event));
        await db.SaveChangesAsync();
        return @event.EventId;
    }

    private static async Task<OutboxMessage> ReadAsync(ServiceProvider provider, Guid eventId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeploymentIntelligenceDbContext>();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventId == eventId);
    }

    private static async Task DeleteAsync(ServiceProvider provider, Guid eventId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeploymentIntelligenceDbContext>();
        await db.OutboxMessages.Where(m => m.EventId == eventId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A_recorded_message_is_published_once_and_marked_sent()
    {
        var publisher = new RecordingPublisher();
        await using var provider = Provider(publisher);
        var eventId = await EnqueueAsync(provider);

        try
        {
            await Relay(provider).RunOnceAsync();

            var call = publisher.Calls.Single(c => c.EventId == eventId);
            Assert.Equal(DeploymentTopics.Deployments, call.Topic);
            Assert.Equal("DeploymentRecorded", call.EventType);
            Assert.Equal(1, call.Version);
            Assert.Contains("v1.0.0", call.PayloadJson);

            var row = await ReadAsync(provider, eventId);
            Assert.NotNull(row.SentAtUtc);
            Assert.Equal(0, row.Attempts);
            Assert.False(row.IsAbandoned);
            Assert.False(row.IsPending(DateTimeOffset.UtcNow));
        }
        finally
        {
            await DeleteAsync(provider, eventId);
        }
    }

    [Fact]
    public async Task A_failed_publish_keeps_the_message_pending_and_records_the_attempt()
    {
        var publisher = new RecordingPublisher(fail: true);
        await using var provider = Provider(publisher, options =>
        {
            options.MaxAttempts = 5;
            options.InitialBackoff = TimeSpan.FromMinutes(5);
        });
        var eventId = await EnqueueAsync(provider);

        try
        {
            await Relay(provider).RunOnceAsync();

            var row = await ReadAsync(provider, eventId);
            Assert.Equal(1, row.Attempts);
            Assert.Null(row.SentAtUtc);
            Assert.False(row.IsAbandoned);
            Assert.NotNull(row.LastError);
            Assert.Contains("broker unavailable", row.LastError!);
            // The retry window is the only thing that stops an instant retry loop,
            // which is what makes a multi-hour broker outage survivable.
            Assert.True(row.NextAttemptAtUtc > DateTimeOffset.UtcNow.AddMinutes(4));
        }
        finally
        {
            await DeleteAsync(provider, eventId);
        }
    }

    [Fact]
    public async Task A_message_that_exhausts_its_retry_budget_is_abandoned_and_never_retried()
    {
        var publisher = new RecordingPublisher(fail: true);
        await using var provider = Provider(publisher, options =>
        {
            options.MaxAttempts = 1;
            options.InitialBackoff = TimeSpan.FromMilliseconds(1);
        });
        var eventId = await EnqueueAsync(provider);

        try
        {
            var relay = Relay(provider);
            await relay.RunOnceAsync();
            await relay.RunOnceAsync();

            Assert.Equal(1, publisher.Calls.Count(c => c.EventId == eventId));

            var row = await ReadAsync(provider, eventId);
            Assert.True(row.IsAbandoned);
            Assert.Equal(1, row.Attempts);
            Assert.False(row.IsPending(DateTimeOffset.UtcNow.AddDays(1)));
        }
        finally
        {
            await DeleteAsync(provider, eventId);
        }
    }

    [Fact]
    public async Task A_message_inside_its_lease_window_is_not_republished()
    {
        var publisher = new RecordingPublisher();
        await using var provider = Provider(publisher, options => options.LeaseDuration = TimeSpan.FromMinutes(5));
        var eventId = await EnqueueAsync(provider);

        try
        {
            // A relay instance that claimed the row and died before publishing
            // leaves exactly this state: the claim is a future NextAttemptAtUtc
            // in the database, not an in-memory flag, so another instance (or a
            // restart) cannot double-publish during the lease.
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DeploymentIntelligenceDbContext>();
                var claimed = await db.OutboxMessages
                    .Where(m => m.EventId == eventId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        m => m.NextAttemptAtUtc, DateTimeOffset.UtcNow.AddMinutes(5)));
                Assert.Equal(1, claimed);
            }

            await Relay(provider).RunOnceAsync();

            Assert.DoesNotContain(publisher.Calls, c => c.EventId == eventId);
            var row = await ReadAsync(provider, eventId);
            Assert.Null(row.SentAtUtc);
        }
        finally
        {
            await DeleteAsync(provider, eventId);
        }
    }
}
