using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Shared.Contracts;

namespace Atlas.Modules.IncidentManagement.Application;

public sealed class IncidentAlertSink : IIncidentAlertSink
{
    private readonly IIncidentService _incidents;
    public IncidentAlertSink(IIncidentService incidents) => _incidents = incidents;

    public async Task<Guid?> RaiseAsync(Guid organizationId, string title, string severity, string source, CancellationToken ct = default)
    {
        var incidentTitle = $"{title} (source: {source})";
        var active = await _incidents.GetActiveIncidentsAsync(organizationId, 1, 100, null, null, ct);
        if (active.Any(i => string.Equals(i.Title, incidentTitle, StringComparison.OrdinalIgnoreCase)))
            return active.First(i => string.Equals(i.Title, incidentTitle, StringComparison.OrdinalIgnoreCase)).Id;
        if (!Enum.TryParse<IncidentSeverity>(severity, true, out var parsedSeverity)) parsedSeverity = IncidentSeverity.Sev2;
        var result = await _incidents.DeclareIncidentAsync(organizationId, incidentTitle, parsedSeverity, DateTimeOffset.UtcNow, Array.Empty<Guid>(), ct);
        return result.IsSuccess ? result.Value : null;
    }
}
