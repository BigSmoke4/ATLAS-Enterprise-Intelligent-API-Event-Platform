using Atlas.Shared.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The 409 contract: a stale write must be reported as an actionable conflict,
/// and every other exception must be left untouched so the platform's normal
/// ProblemDetails handling (and, in Development, the developer page) still sees it.
/// </summary>
public class ConcurrencyConflictExceptionHandlerTests
{
    private static ConcurrencyConflictExceptionHandler Handler() =>
        new(NullLogger<ConcurrencyConflictExceptionHandler>.Instance);

    private static DefaultHttpContext Context(string method, string path)
    {
        var context = new DefaultHttpContext
        {
            // HttpResults resolve JSON options through RequestServices; an empty
            // container keeps the test host faithful to a real request.
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> BodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        var json = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement Property(JsonElement root, string name) =>
        root.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    [Fact]
    public async Task A_stale_write_becomes_a_409_problem_details_with_a_stable_code()
    {
        var context = Context("POST", "/api/v1/incidents/transition");

        var handled = await Handler().TryHandleAsync(
            context, new DbUpdateConcurrencyException("The database operation was expected to affect 1 row(s), but actually affected 0 row(s)."), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);

        var body = await BodyAsync(context);
        Assert.Equal(ConcurrencyConflictExceptionHandler.ErrorCode, Property(body, "code").GetString());
        Assert.Equal(StatusCodes.Status409Conflict, Property(body, "status").GetInt32());
        Assert.Contains("retry", Property(body, "detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_conflict_names_the_aggregate_that_changed_so_an_operator_can_act()
    {
        // A real EF entry, so the handler's metadata handling is exercised rather
        // than a hand-built stub.
        using var db = new Atlas.Modules.EventPlatform.Infrastructure.EventPlatformDbContext(
            new DbContextOptionsBuilder<Atlas.Modules.EventPlatform.Infrastructure.EventPlatformDbContext>()
                .UseInMemoryDatabase($"conflict-{Guid.NewGuid()}")
                .Options);
        var entry = db.Entry(Atlas.Modules.EventPlatform.Domain.DeadLetterEvent.Create(
            "orders", Guid.NewGuid(), "OrderCreated", Guid.NewGuid(), "{}", "boom"));
        var context = Context("POST", "/api/v1/events/dead-letters/1/replay");

        var handled = await Handler().TryHandleAsync(
            context, new DbUpdateConcurrencyException("conflict", new[] { entry }), CancellationToken.None);

        Assert.True(handled);
        var body = await BodyAsync(context);
        var entities = Property(body, "conflictingEntities").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { nameof(Atlas.Modules.EventPlatform.Domain.DeadLetterEvent) }, entities);
    }

    [Fact]
    public async Task Any_other_exception_is_not_claimed_and_no_response_body_is_written()
    {
        var context = Context("POST", "/api/v1/policies");

        var handled = await Handler().TryHandleAsync(context, new InvalidOperationException("not a conflict"), CancellationToken.None);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }
}
