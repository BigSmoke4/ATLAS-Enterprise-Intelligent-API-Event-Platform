using Atlas.Modules.EventPlatform.Infrastructure;
using Xunit;

namespace Atlas.UnitTests;

public class EventHandlerDispatcherTests
{
    [Fact]
    public async Task Dispatches_to_the_registered_handler_for_the_event_type()
    {
        var dispatcher = new EventHandlerDispatcher();
        string? received = null;
        dispatcher.Register("OrderCreated", (payload, ct) => { received = payload; return Task.CompletedTask; });

        var handled = await dispatcher.DispatchAsync("OrderCreated", "{\"orderId\":1}");

        Assert.True(handled);
        Assert.Equal("{\"orderId\":1}", received);
    }

    [Fact]
    public async Task Returns_false_for_unregistered_event_type()
    {
        var dispatcher = new EventHandlerDispatcher();
        var handled = await dispatcher.DispatchAsync("NoSuchEvent", "{}");
        Assert.False(handled);
    }

    [Fact]
    public async Task Later_registration_for_same_type_replaces_the_earlier_one()
    {
        var dispatcher = new EventHandlerDispatcher();
        var callCount = 0;
        dispatcher.Register("X", (p, ct) => { callCount += 1; return Task.CompletedTask; });
        dispatcher.Register("X", (p, ct) => { callCount += 100; return Task.CompletedTask; });

        await dispatcher.DispatchAsync("X", "{}");

        Assert.Equal(100, callCount);
    }
}
