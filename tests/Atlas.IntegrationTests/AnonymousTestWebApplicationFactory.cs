using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;

namespace Atlas.IntegrationTests;

public sealed class AnonymousTestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "AnonymousIntegrationTest";
                options.DefaultChallengeScheme = "AnonymousIntegrationTest";
            }).AddScheme<AuthenticationSchemeOptions, AnonymousIntegrationAuthenticationHandler>("AnonymousIntegrationTest", _ => { });
        });
    }
}

public sealed class AnonymousIntegrationAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public AnonymousIntegrationAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}
