using System.Text.Json;

namespace Atlas.Shared.Contracts;

public static class EventContractValidator
{
    public static void Validate(IIntegrationEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event.EventId == Guid.Empty) throw new ArgumentException("EventId is required.");
        if (string.IsNullOrWhiteSpace(@event.EventType) || @event.EventType.Length > 256) throw new ArgumentException("EventType is required and must be at most 256 characters.");
        if (@event.Version < 1) throw new ArgumentException("Event Version must be positive.");
        if (@event.TimestampUtc > DateTimeOffset.UtcNow.AddMinutes(5)) throw new ArgumentException("Event timestamp is too far in the future.");
        if (@event.CorrelationId == Guid.Empty) throw new ArgumentException("CorrelationId is required.");
        if (string.IsNullOrWhiteSpace(@event.Producer)) throw new ArgumentException("Producer is required.");
    }

    public static void ValidateJson(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Event payload must be a JSON object.");
        RequireGuid(root, "EventId"); RequireString(root, "EventType"); RequirePositiveInt(root, "Version");
        RequireGuid(root, "CorrelationId"); RequireString(root, "Producer"); RequireString(root, "TimestampUtc");
    }

    private static void RequireGuid(JsonElement root, string name)
        => RequireString(root, name, value => Guid.TryParse(value, out var id) && id != Guid.Empty);
    private static void RequireString(JsonElement root, string name, Func<string, bool>? predicate = null)
    {
        var property = root.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        if (string.IsNullOrWhiteSpace(value) || predicate is not null && !predicate(value)) throw new ArgumentException($"Event field {name} is required.");
    }
    private static void RequirePositiveInt(JsonElement root, string name)
    {
        var property = root.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (!property.Value.TryGetInt32(out var value) || value < 1) throw new ArgumentException($"Event field {name} must be positive.");
    }
}
