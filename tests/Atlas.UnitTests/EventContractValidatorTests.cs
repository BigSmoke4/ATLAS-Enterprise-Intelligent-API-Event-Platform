using System.Text.Json;
using Atlas.Shared.Contracts;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The ingress validator is a security boundary, not a formality: anything that
/// passes it reaches the dispatcher, and anything it rejects is dead-lettered
/// without running a handler. These tests pin the hostile-input behaviour —
/// malformed envelopes, empty identifiers, duplicated keys and depth bombs —
/// so a relaxation of the validator cannot pass review silently.
/// </summary>
public sealed class EventContractValidatorTests
{
    private sealed record Envelope(Guid EventId, string EventType, int Version, DateTimeOffset TimestampUtc,
        Guid CorrelationId, Guid? CausationId, string Producer) : IIntegrationEvent;

    private static Envelope Valid() => new(
        Guid.NewGuid(), "OrderCreated", 1, DateTimeOffset.UtcNow, Guid.NewGuid(), null, "tests");

    private static string Payload(string eventId = null!, string correlationId = null!, int version = 1,
        string eventType = "OrderCreated", string producer = "tests", string timestampUtc = null!)
        => $$"""
        {
          "EventId": "{{eventId ?? Guid.NewGuid().ToString()}}",
          "EventType": "{{eventType}}",
          "Version": {{version}},
          "TimestampUtc": "{{timestampUtc ?? DateTimeOffset.UtcNow.ToString("O")}}",
          "CorrelationId": "{{correlationId ?? Guid.NewGuid().ToString()}}",
          "Producer": "{{producer}}"
        }
        """;

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

    [Fact]
    public void A_well_formed_envelope_passes()
    {
        EventContractValidator.Validate(Valid());
        EventContractValidator.ValidateJson(Payload());
    }

    [Fact]
    public void A_null_envelope_is_rejected()
        => Assert.Throws<ArgumentNullException>(() => EventContractValidator.Validate(null!));

    [Fact]
    public void An_empty_identifier_is_rejected()
    {
        var empty = Guid.Empty.ToString();
        Assert.Throws<ArgumentException>(() => EventContractValidator.ValidateJson(Payload(eventId: empty)));
        Assert.Throws<ArgumentException>(() => EventContractValidator.ValidateJson(Payload(correlationId: empty)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_version_is_rejected(int version)
        => Assert.Throws<ArgumentException>(() => EventContractValidator.ValidateJson(Payload(version: version)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_event_type_is_rejected(string eventType)
        => Assert.Throws<ArgumentException>(() => EventContractValidator.ValidateJson(Payload(eventType: eventType)));

    [Fact]
    public void An_event_type_longer_than_the_column_is_rejected()
    {
        // 256 is the persisted column width; accepting 257 here would move the
        // failure to the database, after the handler has already been chosen.
        var envelope = Valid() with { EventType = new string('x', 257) };
        Assert.Throws<ArgumentException>(() => EventContractValidator.Validate(envelope));
    }

    [Fact]
    public void A_timestamp_far_in_the_future_is_rejected()
    {
        var envelope = Valid() with { TimestampUtc = DateTimeOffset.UtcNow.AddHours(1) };
        Assert.Throws<ArgumentException>(() => EventContractValidator.Validate(envelope));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]          // a JSON array is not an event envelope
    [InlineData("\"a string\"")]
    [InlineData("null")]
    public void A_payload_that_is_not_a_json_object_is_rejected(string payload)
        => Assert.ThrowsAny<Exception>(() => EventContractValidator.ValidateJson(payload));

    [Fact]
    public void A_missing_required_field_is_rejected()
    {
        var withoutProducer = $$"""
        { "EventId": "{{Guid.NewGuid()}}", "EventType": "OrderCreated", "Version": 1,
          "TimestampUtc": "{{DateTimeOffset.UtcNow:O}}", "CorrelationId": "{{Guid.NewGuid()}}" }
        """;
        Assert.Throws<ArgumentException>(() => EventContractValidator.ValidateJson(withoutProducer));
    }

    [Fact]
    public void A_depth_bomb_is_rejected_before_it_can_be_walked()
    {
        // JsonDocument's own depth limit (64) is the defence: a deeply nested
        // payload must throw while parsing, not while a recursive handler walks it.
        var bomb = "{\"EventId\":\"" + Guid.NewGuid() + "\",\"Extra\":" + new string('[', 200) + new string(']', 200) + "}";
        Assert.ThrowsAny<Exception>(() => EventContractValidator.ValidateJson(bomb));
    }

    [Fact]
    public void A_duplicated_key_cannot_smuggle_a_second_identity_past_the_check()
    {
        // First occurrence wins. The dangerous shape is a valid-looking value
        // *second*, hoping a later reader takes it — that payload is rejected.
        var duplicated = $$"""
        {
          "EventId": "00000000-0000-0000-0000-000000000000",
          "EventId": "{{Guid.NewGuid()}}",
          "EventType": "OrderCreated", "Version": 1,
          "TimestampUtc": "{{DateTimeOffset.UtcNow:O}}",
          "CorrelationId": "{{Guid.NewGuid()}}", "Producer": "tests"
        }
        """;
        Assert.Throws<ArgumentException>(() => EventContractValidator.ValidateJson(duplicated));
    }
}
