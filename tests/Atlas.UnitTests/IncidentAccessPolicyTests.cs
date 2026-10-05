using Atlas.Modules.IncidentManagement.Application;
using Atlas.Modules.IncidentManagement.Domain;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The per-incident rule: an SRE may change an incident they declared, a
/// privileged role may change any incident, and an incident with no declarer is
/// closed to a plain SRE (fail closed, never fail open).
/// </summary>
public class IncidentAccessPolicyTests
{
    private static Incident DeclaredBy(Guid? declarer)
    {
        var organizationId = Guid.NewGuid();
        return Incident.Detect(organizationId, "Checkout latency spike", IncidentSeverity.Sev2,
            DateTimeOffset.UtcNow.AddMinutes(-5), new[] { Guid.NewGuid() }, declarer);
    }

    [Fact]
    public void The_sre_who_declared_the_incident_may_act_on_it()
    {
        var declarer = Guid.NewGuid();
        Assert.True(IncidentAccessPolicy.CanAct(DeclaredBy(declarer), declarer, actorIsPrivileged: false));
    }

    [Fact]
    public void Another_sre_may_not_act_on_an_incident_they_did_not_declare()
        => Assert.False(IncidentAccessPolicy.CanAct(DeclaredBy(Guid.NewGuid()), Guid.NewGuid(), actorIsPrivileged: false));

    [Theory]
    [InlineData(null)]                                        // unauthenticated / no user claim
    public void A_caller_without_an_identity_cannot_act(Guid? actor)
        => Assert.False(IncidentAccessPolicy.CanAct(DeclaredBy(Guid.NewGuid()), actor, actorIsPrivileged: false));

    [Fact]
    public void A_privileged_role_may_act_on_an_incident_declared_by_someone_else()
        => Assert.True(IncidentAccessPolicy.CanAct(DeclaredBy(Guid.NewGuid()), Guid.NewGuid(), actorIsPrivileged: true));

    [Fact]
    public void An_unattributed_incident_is_closed_to_a_plain_sre_but_open_to_a_privileged_role()
    {
        var unattributed = DeclaredBy(null);
        Assert.False(IncidentAccessPolicy.CanAct(unattributed, Guid.NewGuid(), actorIsPrivileged: false));
        Assert.True(IncidentAccessPolicy.CanAct(unattributed, Guid.NewGuid(), actorIsPrivileged: true));
    }

    [Fact]
    public void The_denial_explains_who_can_act()
    {
        Assert.Contains("declarer", IncidentAccessPolicy.ExplainDenial(DeclaredBy(null)));
        Assert.Contains("OrganizationAdmin", IncidentAccessPolicy.ExplainDenial(DeclaredBy(Guid.NewGuid())));
    }
}
