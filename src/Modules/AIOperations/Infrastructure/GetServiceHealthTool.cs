using Atlas.Modules.AIOperations.Application;
using Atlas.Modules.ServiceRegistry.Application;

namespace Atlas.Modules.AIOperations.Infrastructure;

/// <summary>Real tool: reads actual service health via IServiceHealthService — never fabricated.</summary>
public class GetServiceHealthTool : IAtlasTool
{
    private readonly IServiceHealthService _serviceHealthService;
    public GetServiceHealthTool(IServiceHealthService serviceHealthService) => _serviceHealthService = serviceHealthService;

    public string Name => "GetServiceHealth";
    public string Description => "Returns the current aggregate health and instance counts for every registered service in the organization.";
    public ToolKind Kind => ToolKind.Read;

    public async Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
    {
        var statuses = await _serviceHealthService.GetStatusAsync(organizationId, 1, 50, ct);
        if (statuses.Count == 0)
            return new ToolResult(false, "No services registered.");

        var summary = string.Join("; ", statuses.Select(s =>
            $"{s.Name} is {s.AggregateHealth} ({s.HealthyInstanceCount}/{s.InstanceCount} instances healthy)"));

        return new ToolResult(true, summary);
    }
}
