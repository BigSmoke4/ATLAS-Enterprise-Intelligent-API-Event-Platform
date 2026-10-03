using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;

namespace Atlas.Web.Health;

public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly string _connectionString;
    public PostgresHealthCheck(IConfiguration configuration) => _connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return HealthCheckResult.Unhealthy("PostgreSQL connection string is not configured.");
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or OperationCanceledException)
        { return HealthCheckResult.Unhealthy("PostgreSQL is unavailable."); }
    }
}

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;
    public RedisHealthCheck(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try { await _redis.GetDatabase().PingAsync(); return HealthCheckResult.Healthy("Redis is reachable."); }
        catch (Exception ex) when (ex is RedisException or TimeoutException or OperationCanceledException)
        { return HealthCheckResult.Unhealthy("Redis is unavailable."); }
    }
}

public sealed class KafkaHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    public KafkaHealthCheck(IConfiguration configuration) => _configuration = configuration;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var bootstrap = _configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrap)) return Task.FromResult(HealthCheckResult.Unhealthy("Kafka bootstrap servers are not configured."));
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
            admin.GetMetadata(TimeSpan.FromSeconds(2));
            return Task.FromResult(HealthCheckResult.Healthy("Kafka is reachable."));
        }
        catch (Exception ex) when (ex is KafkaException or TimeoutException)
        { return Task.FromResult(HealthCheckResult.Unhealthy("Kafka is unavailable.")); }
    }
}
