using Atlas.Modules.AIOperations.Application;
using Atlas.Modules.IncidentManagement.Application;

namespace Atlas.Modules.AIOperations.Infrastructure;

/// <summary>Real tool: reads actual active incidents via IIncidentService — never fabricated.</summary>
public class GetIncidentHistoryTool : IAtlasTool
{
    private readonly IIncidentService _incidentService;
    public GetIncidentHistoryTool(IIncidentService incidentService) => _incidentService = incidentService;

    public string Name => "GetIncidentHistory";
    public string Description => "Returns currently active (non-postmortem-complete) incidents for the organization.";
    public ToolKind Kind => ToolKind.Read;

    public async Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
    {
        var incidents = await _incidentService.GetActiveIncidentsAsync(organizationId, 1, 50, ct);
        if (incidents.Count == 0)
            return new ToolResult(false, "No active incidents.");

        var summary = string.Join("; ", incidents.Select(i =>
            $"\"{i.Title}\" ({i.Severity}) is currently {i.Status}, detected {i.DetectedAtUtc:u}"));

        return new ToolResult(true, summary);
    }
}
