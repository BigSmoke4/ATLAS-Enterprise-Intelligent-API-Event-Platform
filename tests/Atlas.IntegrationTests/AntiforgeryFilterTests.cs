using System.Security.Claims;
using Atlas.Web.Middleware;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// The antiforgery filter is deliberately selective: it must validate
/// cookie-authenticated writes and must leave machine/test principals alone.
/// Getting that backwards would either leave JSON writes CSRF-reachable or
/// break every API-key caller, so the skip rules are pinned by test.
///
/// This lives in the integration-test project because it needs the web
/// project's filter; it runs in-process with a stubbed
/// <see cref="IAntiforgery"/>, so it needs no database, broker or browser.
/// </summary>
public sealed class AntiforgeryFilterTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Safe_methods_are_never_validated(string method)
    {
        var antiforgery = new RecordingAntiforgery();
        var context = BuildContext(method, scheme: "Identity.Application", apiKey: false);

        await Filter(antiforgery).OnAuthorizationAsync(context);

        Assert.Equal(0, antiforgery.ValidateCalls);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Cookie_authenticated_write_without_a_token_is_rejected()
    {
        var antiforgery = new RecordingAntiforgery { ThrowOnValidate = true };
        var context = BuildContext("POST", scheme: "Identity.Application", apiKey: false);

        await Filter(antiforgery).OnAuthorizationAsync(context);

        Assert.Equal(1, antiforgery.ValidateCalls);
        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Contains("Antiforgery", problem.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cookie_authenticated_write_with_a_valid_token_is_allowed()
    {
        var antiforgery = new RecordingAntiforgery();
        var context = BuildContext("POST", scheme: "Identity.Application", apiKey: false);

        await Filter(antiforgery).OnAuthorizationAsync(context);

        Assert.Equal(1, antiforgery.ValidateCalls);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Api_key_callers_are_exempt()
    {
        var antiforgery = new RecordingAntiforgery { ThrowOnValidate = true };
        var context = BuildContext("POST", scheme: "ApiKey", apiKey: true);

        await Filter(antiforgery).OnAuthorizationAsync(context);

        Assert.Equal(0, antiforgery.ValidateCalls);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Non_cookie_schemes_are_exempt()
    {
        var antiforgery = new RecordingAntiforgery { ThrowOnValidate = true };
        var context = BuildContext("POST", scheme: "IntegrationTest", apiKey: false);

        await Filter(antiforgery).OnAuthorizationAsync(context);

        Assert.Equal(0, antiforgery.ValidateCalls);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Anonymous_requests_are_left_to_the_authorization_middleware()
    {
        var antiforgery = new RecordingAntiforgery { ThrowOnValidate = true };
        var context = BuildContext("POST", scheme: null, apiKey: false);

        await Filter(antiforgery).OnAuthorizationAsync(context);

        Assert.Equal(0, antiforgery.ValidateCalls);
        Assert.Null(context.Result);
    }

    private static ValidateAntiforgeryForCookieAuthFilter Filter(IAntiforgery antiforgery)
        => new(antiforgery, NullLogger<ValidateAntiforgeryForCookieAuthFilter>.Instance);

    private static AuthorizationFilterContext BuildContext(string method, string? scheme, bool apiKey)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        if (apiKey) httpContext.Request.Headers["X-Api-Key"] = "atlas_test_key";

        if (scheme is not null)
        {
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, scheme);
            httpContext.User = new ClaimsPrincipal(identity);
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    private sealed class RecordingAntiforgery : IAntiforgery
    {
        public int ValidateCalls { get; private set; }
        public bool ThrowOnValidate { get; init; }

        public Task ValidateRequestAsync(HttpContext httpContext)
        {
            ValidateCalls++;
            return ThrowOnValidate
                ? Task.FromException(new AntiforgeryValidationException("The antiforgery token could not be decrypted."))
                : Task.CompletedTask;
        }

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext)
            => new("request", "cookie", "form", "header");

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext)
            => new("request", "cookie", "form", "header");

        public Task<bool> IsRequestValidAsync(HttpContext httpContext)
            => Task.FromResult(!ThrowOnValidate);

        public void SetCookieTokenAndHeader(HttpContext httpContext)
        {
        }
    }
}
