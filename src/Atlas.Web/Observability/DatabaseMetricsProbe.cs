using Npgsql;

namespace Atlas.Web.Observability;

/// <summary>
/// PostgreSQL health/performance snapshot read from the server's own
/// statistics views (<c>pg_stat_activity</c>, <c>pg_stat_database</c>,
/// <c>pg_settings</c>).
///
/// Why this exists: the master prompt requires database connection and
/// query metrics on the observability surface, and the honest way to get them
/// from a managed PostgreSQL is to ask PostgreSQL. Nothing here is estimated
/// or synthesized — when the database is unreachable the probe returns null
/// and the API reports the dependency as unavailable instead of a zero.
/// </summary>
public sealed record DatabaseMetricsSnapshot(
    int TotalConnections,
    int ActiveConnections,
    int IdleConnections,
    int IdleInTransactionConnections,
    int MaxConnections,
    double LongestActiveQuerySeconds,
    long TransactionsCommitted,
    long TransactionsRolledBack,
    double BlockCacheHitRatio,
    long Deadlocks,
    DateTimeOffset CapturedAtUtc)
{
    public double ConnectionUtilization => MaxConnections <= 0 ? 0d : TotalConnections / (double)MaxConnections;
}

public interface IDatabaseMetricsProbe
{
    /// <returns>The snapshot, or null when PostgreSQL could not be queried.</returns>
    Task<DatabaseMetricsSnapshot?> SnapshotAsync(CancellationToken ct = default);
}

public sealed class DatabaseMetricsProbe : IDatabaseMetricsProbe
{
    private const string MetricsSql = """
        select
            (select count(*) from pg_stat_activity where datname = current_database())::bigint as total_connections,
            (select count(*) from pg_stat_activity where datname = current_database() and state = 'active')::bigint as active_connections,
            (select count(*) from pg_stat_activity where datname = current_database() and state = 'idle')::bigint as idle_connections,
            (select count(*) from pg_stat_activity where datname = current_database() and state = 'idle in transaction')::bigint as idle_in_transaction,
            (select setting::int from pg_settings where name = 'max_connections') as max_connections,
            (select coalesce(max(extract(epoch from (now() - query_start))), 0)::float8
               from pg_stat_activity
              where datname = current_database() and state = 'active' and pid <> pg_backend_pid()) as longest_active_query_seconds,
            (select xact_commit from pg_stat_database where datname = current_database())::bigint as xact_commit,
            (select xact_rollback from pg_stat_database where datname = current_database())::bigint as xact_rollback,
            (select case when (blks_hit + blks_read) = 0 then 0
                         else blks_hit::float8 / (blks_hit + blks_read) end
               from pg_stat_database where datname = current_database()) as block_cache_hit_ratio,
            (select deadlocks from pg_stat_database where datname = current_database())::bigint as deadlocks
        """;

    private readonly string _connectionString;
    private readonly TimeSpan _cacheFor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DatabaseMetricsSnapshot? _cached;

    public DatabaseMetricsProbe(IConfiguration configuration, TimeSpan? cacheFor = null)
    {
        _connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;
        _cacheFor = cacheFor ?? TimeSpan.FromSeconds(5);
    }

    public async Task<DatabaseMetricsSnapshot?> SnapshotAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return null;

        if (_cached is not null && DateTimeOffset.UtcNow - _cached.CapturedAtUtc < _cacheFor) return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow - _cached.CapturedAtUtc < _cacheFor) return _cached;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(MetricsSql, connection) { CommandTimeout = 5 };
            await using var reader = await command.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct)) return null;

            _cached = new DatabaseMetricsSnapshot(
                TotalConnections: (int)reader.GetInt64(0),
                ActiveConnections: (int)reader.GetInt64(1),
                IdleConnections: (int)reader.GetInt64(2),
                IdleInTransactionConnections: (int)reader.GetInt64(3),
                MaxConnections: reader.GetInt32(4),
                LongestActiveQuerySeconds: reader.GetDouble(5),
                TransactionsCommitted: reader.GetInt64(6),
                TransactionsRolledBack: reader.GetInt64(7),
                BlockCacheHitRatio: reader.GetDouble(8),
                Deadlocks: reader.GetInt64(9),
                CapturedAtUtc: DateTimeOffset.UtcNow);

            return _cached;
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            // Unavailable is reported as "no data" and surfaced by the health
            // check — never as a fabricated zero.
            _cached = null;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
