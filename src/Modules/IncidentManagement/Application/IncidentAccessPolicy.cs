using Atlas.Modules.IncidentManagement.Domain;

namespace Atlas.Modules.IncidentManagement.Application;

/// <summary>
/// Per-incident authorization, on top of the tenant/role checks the pipeline
/// already performs.
///
/// Tenant scope answers "may this caller see this organization's incidents";
/// the role policy answers "may this caller write incidents at all". Neither
/// answers "may this caller write *this* incident". The rule here is the
/// smallest one that is defensible in an incident review:
///
/// <list type="bullet">
///   <item><b>PlatformAdmin / OrganizationAdmin</b> may act on any incident in
///   scope — someone has to be able to take over an incident whose declarer is
///   unavailable;</item>
///   <item><b>SRE</b> may act on incidents they declared. Declaring is
///   attribution, not ownership of the customer's problem, so any SRE can still
///   declare and any privileged role can still act.</item>
/// </list>
///
/// Incidents with no declarer on record (created before attribution existed, or
/// by an automated path without a user) are <b>not</b> writable by a plain SRE:
/// the rule fails closed and the privileged roles remain the escape hatch.
/// </summary>
public static class IncidentAccessPolicy
{
    public static bool CanAct(Incident incident, Guid? actorUserId, bool actorIsPrivileged)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (actorIsPrivileged) return true;
        if (actorUserId is null) return false;
        return incident.DeclaredByUserId is { } declarer && declarer == actorUserId.Value;
    }

    /// <summary>Human-readable reason, used in the 403 body so an operator knows who to ask.</summary>
    public static string ExplainDenial(Incident incident) =>
        incident.DeclaredByUserId is null
            ? "This incident has no declarer on record, so only a PlatformAdmin or OrganizationAdmin may change it."
            : "Only the SRE who declared this incident (or a PlatformAdmin/OrganizationAdmin) may change it.";
}
