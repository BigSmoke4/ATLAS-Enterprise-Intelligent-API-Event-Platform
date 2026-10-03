using System.Text.Json;
using Atlas.Shared.Contracts;
using Xunit;

namespace Atlas.UnitTests;

public sealed class EventContractValidatorTests
{
    [Fact]
    public void Rejects_malformed_event_json()
    {
        Assert.Throws<ArgumentException>(() => EventContractValidator.ValidateJson("{\"EventType\":\"OrderCreated\"}"));
    }

    [Fact]
    public void Accepts_required_versioned_event_metadata()
    {
        var json = JsonSerializer.Serialize(new
        {
            EventId = Guid.NewGuid(), EventType = "OrderCreated", Version = 1,
            TimestampUtc = DateTimeOffset.UtcNow, CorrelationId = Guid.NewGuid(), Producer = "orders"
        });
        EventContractValidator.ValidateJson(json);
    }
}
