using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Atlas.IntegrationTests;

public sealed class AnonymousTestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
