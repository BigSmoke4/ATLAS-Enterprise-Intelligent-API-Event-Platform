namespace Atlas.Shared.Contracts;

public interface IIncidentAlertSink
{
    Task<Guid?> RaiseAsync(Guid organizationId, string title, string severity, string source, CancellationToken ct = default);
}
