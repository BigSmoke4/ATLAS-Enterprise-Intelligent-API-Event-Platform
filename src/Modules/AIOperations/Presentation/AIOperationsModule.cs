using Atlas.Modules.AIOperations.Application;
using Atlas.Modules.AIOperations.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.AIOperations.Presentation;

/// <summary>
/// STATUS: real end-to-end for both read evidence-gathering AND action
/// execution. Three real read tools (GetServiceHealth, GetIncidentHistory,
/// GetSLOStatus) plus one real action tool (DeactivatePolicy — genuinely
/// deactivates a PolicyEngine rule). Every action tool call MUST go through
/// AiOperationsAssistant.ExecuteActionAsync, which checks ActionToolGuard
/// first (unauthorized or unconfirmed calls are denied, not silently
/// skipped). AnthropicCompletionClient makes a real HTTP call to the
/// Anthropic API for prose generation, registered only when
/// AI:AnthropicApiKey is configured in appsettings/secrets — without a key,
/// AnswerAsync falls back to a deterministic evidence-only summary rather
/// than failing.
/// </summary>
public class AIOperationsModule : IAtlasModule
{
    public string Name => "AIOperations";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ActionToolGuard>();

        services.AddScoped<IAtlasTool, GetServiceHealthTool>();
        services.AddScoped<IAtlasTool, GetIncidentHistoryTool>();
        services.AddScoped<IAtlasTool, GetSloStatusTool>();
        services.AddScoped<IAtlasTool, DeactivatePolicyTool>();

        var apiKey = configuration["AI:AnthropicApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            services.AddSingleton(new AnthropicCompletionClientOptions { ApiKey = apiKey });
            services.AddHttpClient<IAiCompletionClient, AnthropicCompletionClient>();
        }
        // If no API key is configured, no IAiCompletionClient is registered
        // — AiOperationsAssistant handles that (falls back to a
        // deterministic evidence summary) rather than crashing.

        services.AddScoped<AiOperationsAssistant>(sp =>
            new AiOperationsAssistant(sp.GetServices<IAtlasTool>(), sp.GetRequiredService<ActionToolGuard>(), sp.GetService<IAiCompletionClient>()));
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // /api/v1/ai/ask and /api/v1/ai/actions mapped via Atlas.Web/Controllers/AiController.
    }
}
