using Atlas.Modules.AIOperations.Presentation;
using Atlas.Modules.APIManagement.Presentation;
using Atlas.Modules.Audit.Presentation;
using Atlas.Modules.DeploymentIntelligence.Presentation;
using Atlas.Modules.EventPlatform.Presentation;
using Atlas.Modules.Identity.Presentation;
using Atlas.Modules.IncidentManagement.Presentation;
using Atlas.Modules.Observability.Presentation;
using Atlas.Modules.Organizations.Presentation;
using Atlas.Modules.PolicyEngine.Presentation;
using Atlas.Modules.Reliability.Application;
using Atlas.Modules.Reliability.Presentation;
using Atlas.Modules.ServiceRegistry.Presentation;
using Atlas.Modules.TrafficManagement.Presentation;
using Atlas.Shared.Contracts;
using Atlas.Shared.Web;
using Serilog;
using Atlas.Web.Middleware;
using Atlas.Web.Hubs;
using Atlas.Web.Health;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using OpenTelemetry.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Host.UseSerilog();

// Every module is registered through the same IAtlasModule seam. Atlas.Web
// never reaches into a module's Domain/Infrastructure namespaces directly —
// only Presentation/*Module.cs, which is each module's public surface.
var modules = new List<IAtlasModule>
{
    new IdentityModule(),
    new OrganizationsModule(),
    new APIManagementModule(),
    new TrafficManagementModule(),
    new ServiceRegistryModule(),
    new EventPlatformModule(),
    new ReliabilityModule(),
    new ObservabilityModule(),
    new IncidentManagementModule(),
    new DeploymentIntelligenceModule(),
    new PolicyEngineModule(),
    new AIOperationsModule(),
    new AuditModule(),
};

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
})
.AddCookie(IdentityConstants.ApplicationScheme, options =>
{
    options.Cookie.Name = "atlas.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/denied";
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
        context.Response.Redirect(context.RedirectUri); return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; }
        context.Response.Redirect(context.RedirectUri); return Task.CompletedTask;
    };
});
builder.Services.AddHealthChecks()
    .AddCheck("process", () => HealthCheckResult.Healthy("Process is alive."), tags: new[] { "live" })
    .AddCheck<PostgresHealthCheck>("postgres", tags: new[] { "ready" })
    .AddCheck<RedisHealthCheck>("redis", tags: new[] { "ready" })
    .AddCheck<KafkaHealthCheck>("kafka", tags: new[] { "ready" });
builder.Services.AddAntiforgery(options =>
{
    // Real CSRF protection for the Razor pages (Services/Incidents/Dashboard).
    // JSON API clients using an API key (X-Api-Key-Prefix) are a separate
    // auth mechanism, not cookie-based sessions, so they aren't CSRF-exposed
    // the same way — but any future Razor <form> POST must include
    // @Html.AntiForgeryToken() / [ValidateAntiForgeryToken] to use this.
    options.HeaderName = "X-CSRF-TOKEN";
});
builder.Services.AddSingleton(modules as IReadOnlyList<IAtlasModule>);

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

// Example of real circuit-breaker enforcement on an outbound call: any
// downstream service ATLAS proxies to should register a named HttpClient
// this way so CircuitBreakerDelegatingHandler actually guards it. This one
// is a template — point it at a real downstream base address to use it.
builder.Services.AddHttpClient("downstream-example", client =>
    {
        // client.BaseAddress = new Uri("https://example-downstream.internal");
    })
    .AddHttpMessageHandler(sp =>
        new CircuitBreakerDelegatingHandler(sp.GetRequiredService<ICircuitBreakerRegistry>(), "downstream-example"));

var app = builder.Build();

// API clients must receive an authentication response rather than an HTML
// HTTPS redirect when no credentials are present. Authenticated API clients
// still pass through normal HTTPS enforcement below.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Headers.ContainsKey("X-Api-Key") &&
        !context.Request.IsHttps)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseExceptionHandler();
    app.UseHsts();
}

if (!app.Environment.IsEnvironment("Testing"))
    app.UseHttpsRedirection();
app.UseAtlasSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Headers.ContainsKey("X-Api-Key") &&
        context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});
if (!app.Environment.IsEnvironment("Testing"))
    app.UseAtlasRateLimiting();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapHub<IncidentHub>("/hubs/incidents");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

foreach (var module in modules)
{
    module.RegisterEndpoints(app);
}

app.Run();

/// <summary>Exposed so Atlas.IntegrationTests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program { }
