using System.Text.Json;
using Atlas.Shared.Contracts;
using Atlas.Shared.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.EventPlatform.Infrastructure;

/// <summary>
/// Registers the platform's consumer-side handlers with
/// <see cref="IEventHandlerRegistry"/>.
///
/// Handlers are keyed by event type <em>string</em> and receive the raw JSON
/// payload, which is what keeps EventPlatform free of any business module's
/// types: the module that owns a reaction registers it here, and the Kafka
/// consumer stays generic (no switch over event types, no cross-module
/// reference). Registration happens in a hosted service so it is independent
/// of module registration order and only runs in a real host (never in tests,
/// where the dispatcher is constructed directly).
/// </summary>
public sealed class EventPlatformHandlerRegistration : IHostedService
{
    internal const string DeploymentRecordedEventType = "DeploymentRecorded";

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IEventHandlerRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EventPlatformHandlerRegistration> _logger;

    public EventPlatformHandlerRegistration(
        IEventHandlerRegistry registry,
        IServiceScopeFactory scopeFactory,
        ILogger<EventPlatformHandlerRegistration> logger)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // DeploymentRecorded: record that the platform observed the deployment
        // through the event pipeline, as an append-only audit entry. The
        // idempotency guard has already de-duplicated by event id before the
        // handler runs, so a redelivery cannot produce a second entry.
        _registry.Register(DeploymentRecordedEventType, HandleDeploymentRecordedAsync);

        _logger.LogInformation("Event handler registry: {Count} handler(s) registered ({Handlers}).",
            1, DeploymentRecordedEventType);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task HandleDeploymentRecordedAsync(string payloadJson, CancellationToken ct)
    {
        DeploymentRecordedPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<DeploymentRecordedPayload>(payloadJson, PayloadOptions);
        }
        catch (JsonException ex)
        {
            // A malformed payload must fail loudly so the consumer's retry
            // policy can exhaust and dead-letter it — silently ignoring it
            // would lose the event without a trace.
            throw new InvalidOperationException("DeploymentRecorded payload was not valid JSON.", ex);
        }

        if (payload is null || payload.OrganizationId == Guid.Empty || payload.DeploymentId == Guid.Empty)
        {
            throw new InvalidOperationException("DeploymentRecorded payload is missing organizationId or deploymentId.");
        }

        using var scope = _scopeFactory.CreateScope();
        var audit = scope.ServiceProvider.GetService<IAuditSink>();
        if (audit is not null)
        {
            var afterJson = JsonSerializer.Serialize(new
            {
                payload.DeploymentVersion,
                payload.Environment,
                payload.CommitSha,
                payload.Author,
                payload.ServiceId
            });

            await audit.RecordAsync(new AuditRecord(
                ActorUserId: null,
                ActorDisplay: "event-consumer",
                OrganizationId: payload.OrganizationId,
                Action: "deployment.observed",
                ResourceType: "Deployment",
                ResourceId: payload.DeploymentId.ToString(),
                CorrelationId: payload.CorrelationId == Guid.Empty ? Guid.NewGuid() : payload.CorrelationId,
                BeforeJson: null,
                AfterJson: afterJson,
                IpAddress: null), ct);
        }

        AtlasMetrics.EventsConsumed.Add(1,
            new KeyValuePair<string, object?>("event_type", DeploymentRecordedEventType),
            new KeyValuePair<string, object?>("outcome", "handled"));
    }

    /// <summary>Envelope + body of a DeploymentRecorded event as published by DeploymentIntelligence.</summary>
    private sealed record DeploymentRecordedPayload(
        Guid OrganizationId,
        Guid DeploymentId,
        Guid ServiceId,
        string? DeploymentVersion,
        string? Environment,
        string? CommitSha,
        string? Author,
        Guid CorrelationId);
}
