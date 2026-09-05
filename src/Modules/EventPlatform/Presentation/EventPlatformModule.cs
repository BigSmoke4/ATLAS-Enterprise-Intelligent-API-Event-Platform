using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.EventPlatform.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.EventPlatform.Presentation;

/// <summary>
/// STATUS: real end-to-end for the pieces this module owns — publish
/// (KafkaEventPublisher), idempotency (DB-unique-index IdempotencyGuard),
/// AND now the consumer side: KafkaEventConsumer (retry with exponential
/// backoff, then DLQ routing via IDeadLetterService), registered as a
/// BackgroundService whenever Kafka:BootstrapServers + Kafka:Topics are
/// configured. Modules that want to consume events register a handler via
/// IEventHandlerRegistry.Register(eventType, handler) in their own
/// RegisterServices. NOT implemented: operator-driven replay (dry-run vs.
/// live) of dead-lettered events — DeadLetterService can list and
/// mark-replayed, but nothing re-publishes a replayed message yet.
/// </summary>
public class EventPlatformModule : IAtlasModule
{
    public string Name => "EventPlatform";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<EventPlatformDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "eventplatform")));

        services.AddScoped<IIdempotencyGuard, IdempotencyGuard>();
        services.AddScoped<IDeadLetterService, DeadLetterService>();

        var dispatcher = new EventHandlerDispatcher();
        services.AddSingleton<IEventHandlerRegistry>(dispatcher);
        services.AddSingleton<IEventHandlerDispatcher>(dispatcher);

        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (!string.IsNullOrWhiteSpace(bootstrapServers))
        {
            services.AddSingleton<IEventPublisher>(sp =>
                new KafkaEventPublisher(bootstrapServers, sp.GetRequiredService<ILogger<KafkaEventPublisher>>()));

            var topics = configuration.GetSection("Kafka:Topics").Get<string[]>() ?? Array.Empty<string>();
            if (topics.Length > 0)
            {
                services.Configure<KafkaConsumerOptions>(opt =>
                {
                    opt.BootstrapServers = bootstrapServers;
                    opt.ConsumerGroup = configuration["Kafka:ConsumerGroup"] ?? "atlas-default";
                    opt.Topics = topics;
                });
                services.AddHostedService<KafkaEventConsumer>();
            }
        }
        // If Kafka isn't configured, no IEventPublisher/consumer is
        // registered — callers requesting IEventPublisher get a clear DI
        // resolution failure rather than a publisher that silently no-ops.
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // /api/v1/events (DLQ listing/replay) mapped via Atlas.Web/Controllers/EventsController.
    }
}
