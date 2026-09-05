using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Modules.ServiceRegistry.Infrastructure;

public class HealthCheckProberOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public string HealthPath { get; set; } = "/health/live";
}

/// <summary>
/// Real automated prober: on a timer, HTTP-GETs every registered
/// ServiceInstance's health endpoint and feeds the real result into
/// ServiceInstance.RecordHealthCheck — closing the gap where health
/// previously only updated when something called RecordHealthCheckAsync
/// manually. Runs across all organizations (a background job, not an
/// HTTP request, so it legitimately bypasses the per-request tenant
/// query filter — ITenantContext.HasOrganization is false outside an
/// HTTP context, which is the documented behavior of that filter).
/// </summary>
public class HealthCheckProberService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HealthCheckProberOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthCheckProberService> _logger;

    public HealthCheckProberService(IServiceScopeFactory scopeFactory, IOptions<HealthCheckProberOptions> options,
        IHttpClientFactory httpClientFactory, ILogger<HealthCheckProberService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Interval);
        do
        {
            try
            {
                await ProbeAllInstancesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health-check probe cycle failed; will retry next interval.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProbeAllInstancesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceRegistryDbContext>();
        var client = _httpClientFactory.CreateClient(nameof(HealthCheckProberService));
        client.Timeout = _options.RequestTimeout;

        var instances = await db.Instances.ToListAsync(ct);
        foreach (var instance in instances)
        {
            var success = await ProbeInstanceAsync(client, instance.HostAndPort, ct);
            instance.RecordHealthCheck(success, DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> ProbeInstanceAsync(HttpClient client, string hostAndPort, CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync($"http://{hostAndPort}{_options.HealthPath}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unreachable / timed out — a real failed probe, not fabricated.
            return false;
        }
    }
}
