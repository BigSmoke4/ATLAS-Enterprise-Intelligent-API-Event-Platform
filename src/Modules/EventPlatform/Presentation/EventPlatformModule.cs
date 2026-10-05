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
/// RegisterServices. Dead-letter inspection supports topic, event type,
/// time-window, and event-id filters; dry-run and live replay are both
/// authorization-gated at the HTTP layer.
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

        // Consumer handlers are registered at host start (see the class docs);
        // without this the dispatcher would exist but dispatch nothing.
        services.AddHostedService<EventPlatformHandlerRegistration>();

        // Consumer-lag registry: always registered so the metrics endpoint can
        // answer honestly ("no consumer running") instead of 404/500 when Kafka
        // is not configured.
        var consumerGroupName = configuration["Kafka:ConsumerGroup"] ?? "atlas-default";
        services.AddSingleton(new ConsumerLagRegistry(consumerGroupName));
        services.AddSingleton<IConsumerLagReporter>(sp => sp.GetRequiredService<ConsumerLagRegistry>());
        services.AddSingleton<IConsumerLagService>(sp => sp.GetRequiredService<ConsumerLagRegistry>());

        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (!string.IsNullOrWhiteSpace(bootstrapServers))
        {
            services.AddSingleton<KafkaEventPublisher>(sp =>
                new KafkaEventPublisher(bootstrapServers, sp.GetRequiredService<ILogger<KafkaEventPublisher>>()));
            services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<KafkaEventPublisher>());
            services.AddSingleton<IRawEventPublisher>(sp => sp.GetRequiredService<KafkaEventPublisher>());

            var topics = configuration.GetSection("Kafka:Topics").Get<string[]>() ?? Array.Empty<string>();
            if (topics.Length > 0 && configuration["ASPNETCORE_ENVIRONMENT"] != "Testing")
            {
                services.Configure<KafkaConsumerOptions>(opt =>
                {
                    opt.BootstrapServers = bootstrapServers;
                    opt.ConsumerGroup = consumerGroupName;
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
