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
builder.Services.AddHealthChecks();
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAtlasSecurityHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseAtlasRateLimiting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health");

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
