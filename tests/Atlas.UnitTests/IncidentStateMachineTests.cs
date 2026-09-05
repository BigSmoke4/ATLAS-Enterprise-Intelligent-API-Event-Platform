using Atlas.Modules.IncidentManagement.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class IncidentStateMachineTests
{
    [Fact]
    public void New_incident_starts_in_Detected_state()
    {
        var incident = Incident.Detect(Guid.NewGuid(), "Payment API elevated errors", IncidentSeverity.Sev2,
            DateTimeOffset.UtcNow.AddMinutes(-10), new[] { Guid.NewGuid() });

        Assert.Equal(IncidentStatus.Detected, incident.Status);
        Assert.Single(incident.Timeline);
    }

    [Fact]
    public void Cannot_skip_states()
    {
        var incident = Incident.Detect(Guid.NewGuid(), "Orders API down", IncidentSeverity.Sev1,
            DateTimeOffset.UtcNow, Array.Empty<Guid>());

        Assert.Throws<InvalidOperationException>(() => incident.TransitionTo(IncidentStatus.Resolved, "skip ahead"));
    }

    [Fact]
    public void Cannot_move_backward()
    {
        var incident = Incident.Detect(Guid.NewGuid(), "Inventory lag", IncidentSeverity.Sev3,
            DateTimeOffset.UtcNow, Array.Empty<Guid>());
        incident.TransitionTo(IncidentStatus.Investigating, "investigating");
        incident.TransitionTo(IncidentStatus.Mitigating, "mitigating");

        Assert.Throws<InvalidOperationException>(() => incident.TransitionTo(IncidentStatus.Investigating, "back up"));
    }

    [Fact]
    public void Full_lifecycle_computes_mttd_and_mttr()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var incident = Incident.Detect(Guid.NewGuid(), "Billing errors", IncidentSeverity.Sev2, startedAt, Array.Empty<Guid>());

        Assert.True(incident.MeanTimeToDetect >= TimeSpan.FromMinutes(29));
        Assert.Null(incident.MeanTimeToResolve);

        incident.TransitionTo(IncidentStatus.Investigating, "investigating");
        incident.TransitionTo(IncidentStatus.Mitigating, "mitigating");
        incident.TransitionTo(IncidentStatus.Resolved, "resolved");

        Assert.NotNull(incident.MeanTimeToResolve);
        Assert.True(incident.MeanTimeToResolve!.Value >= TimeSpan.Zero);

        incident.CompletePostmortem("https://wiki.internal/postmortems/billing-errors");
        Assert.Equal(IncidentStatus.PostmortemComplete, incident.Status);
    }

    [Fact]
    public void Postmortem_cannot_be_completed_before_resolution()
    {
        var incident = Incident.Detect(Guid.NewGuid(), "Cache misses spiking", IncidentSeverity.Sev4,
            DateTimeOffset.UtcNow, Array.Empty<Guid>());

        Assert.Throws<InvalidOperationException>(() => incident.CompletePostmortem("https://wiki.internal/x"));
    }
}
