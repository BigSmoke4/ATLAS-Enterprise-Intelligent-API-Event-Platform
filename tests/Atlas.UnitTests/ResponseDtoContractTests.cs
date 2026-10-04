using Atlas.Modules.Audit.Application;
using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.IncidentManagement.Application;
using Atlas.Modules.PolicyEngine.Application;
using Atlas.Shared.Domain;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The read endpoints must publish Application DTOs, never EF aggregates: an
/// entity carries persistence state (the concurrency token), a domain-event
/// collector and its tenant column, and every one of those leaks the moment an
/// entity is serialized. This test fails if a DTO grows a member that is an
/// entity or an EF Core type, so the boundary cannot erode silently.
/// </summary>
public class ResponseDtoContractTests
{
    private static readonly Type[] ReadEndpointDtos =
    {
        typeof(IncidentDto),
        typeof(DeploymentDto),
        typeof(DeadLetterEventDto),
        typeof(AuditEntryDto),
        typeof(PolicyRuleDto),
    };

    [Fact]
    public void Read_endpoint_dtos_live_in_their_module_application_layer()
    {
        foreach (var dto in ReadEndpointDtos)
        {
            Assert.StartsWith("Atlas.Modules.", dto.Namespace!, StringComparison.Ordinal);
            Assert.EndsWith(".Application", dto.Namespace!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_dto_member_exposes_an_entity_or_an_ef_core_type()
    {
        var violations = new List<string>();
        foreach (var dto in ReadEndpointDtos)
        {
            foreach (var property in dto.GetProperties())
            {
                if (property.Name is nameof(Entity.RowVersion) or nameof(Entity.DomainEvents))
                    violations.Add($"{dto.Name}.{property.Name} exposes persistence/domain state");

                Collect(property.PropertyType, $"{dto.Name}.{property.Name}", violations);
            }
        }

        Assert.True(violations.Count == 0, "DTO wire-shape violations:\n - " + string.Join("\n - ", violations));
    }

    [Fact]
    public void A_dto_projection_carries_the_read_model_fields_and_leaves_aggregate_state_behind()
    {
        var incident = Atlas.Modules.IncidentManagement.Domain.Incident.Detect(
            Guid.NewGuid(), "Checkout latency spike",
            Atlas.Modules.IncidentManagement.Domain.IncidentSeverity.Sev2,
            DateTimeOffset.UtcNow.AddMinutes(-9),
            new[] { Guid.NewGuid() });
        incident.TransitionTo(Atlas.Modules.IncidentManagement.Domain.IncidentStatus.Investigating, "acknowledged");

        var dto = IncidentDto.From(incident);

        Assert.Equal(incident.Id, dto.Id);
        Assert.Equal(incident.Title, dto.Title);
        Assert.Equal(incident.Status, dto.Status);
        Assert.Single(dto.AffectedServiceIds);
        // The timeline is an entity collection in the model and a value on the wire.
        // Detect() records the detection entry, so an acknowledged incident carries
        // both that entry and the transition note.
        Assert.Equal(2, dto.Timeline.Count);
        var acknowledgement = dto.Timeline.Single(entry =>
            entry.Note.Contains("acknowledged", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(nameof(Atlas.Modules.IncidentManagement.Domain.IncidentStatus.Investigating),
            acknowledgement.EntryType);
        // MTTD/MTTR are derived from recorded timestamps, so an open incident
        // reports a null MTTR rather than a placeholder.
        Assert.True(dto.MeanTimeToDetect > TimeSpan.Zero);
        Assert.Null(dto.MeanTimeToResolve);
        Assert.DoesNotContain(dto.GetType().GetProperties(), p => p.Name == nameof(Entity.RowVersion));
    }

    private static void Collect(Type type, string path, List<string> violations)
    {
        if (typeof(Entity).IsAssignableFrom(type))
            violations.Add($"{path} exposes the entity {type.Name}");

        if (type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true)
            violations.Add($"{path} exposes the EF Core type {type.Name}");

        if (type.IsArray && type.GetElementType() is { } element) Collect(element, path, violations);
        foreach (var argument in type.GetGenericArguments()) Collect(argument, path, violations);
    }
}
