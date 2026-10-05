using System.Diagnostics.Metrics;

namespace Atlas.Shared.Observability;

/// <summary>
/// The single <see cref="Meter"/> for ATLAS-defined instruments. Everything
/// ATLAS measures itself (as opposed to what the runtime/ASP.NET Core
/// instrumentation measures) is declared here so the metric surface is
/// discoverable in one place, and so the OpenTelemetry pipeline
/// (<c>.AddMeter(AtlasMetrics.MeterName)</c>) exports it to Prometheus.
///
/// Rule: these instruments are only ever incremented where the real event
/// occurred. There is no path to set a metric to an invented value.
/// </summary>
public static class AtlasMetrics
{
    public const string MeterName = "Atlas";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // ---- HTTP / request telemetry -------------------------------------------------

    public static readonly Counter<long> HttpRequests = Meter.CreateCounter<long>(
        "atlas.http.requests",
        unit: "{request}",
        description: "HTTP requests observed by the ATLAS telemetry middleware.");

    public static readonly Counter<long> HttpServerErrors = Meter.CreateCounter<long>(
        "atlas.http.request.errors",
        unit: "{request}",
        description: "HTTP requests that completed with a 5xx status code.");

    public static readonly Counter<long> HttpClientErrors = Meter.CreateCounter<long>(
        "atlas.http.request.client_errors",
        unit: "{request}",
        description: "HTTP requests that completed with a 4xx status code.");

    public static readonly Histogram<double> HttpRequestDuration = Meter.CreateHistogram<double>(
        "atlas.http.request.duration",
        unit: "ms",
        description: "End-to-end HTTP request duration observed by the telemetry middleware.");

    public static readonly Counter<long> TelemetrySamplesDropped = Meter.CreateCounter<long>(
        "atlas.telemetry.samples.dropped",
        unit: "{sample}",
        description: "Telemetry observations dropped because the ingest buffer was saturated (back-pressure is never applied to the request path).");

    public static readonly Counter<long> TelemetryFlushFailures = Meter.CreateCounter<long>(
        "atlas.telemetry.flush.failures",
        unit: "{flush}",
        description: "Telemetry aggregation flushes that failed (e.g. PostgreSQL unavailable). Buffered aggregates are retained and retried.");

    public static readonly Counter<long> TelemetryBucketsPersisted = Meter.CreateCounter<long>(
        "atlas.telemetry.buckets.persisted",
        unit: "{bucket}",
        description: "One-minute telemetry buckets merged into PostgreSQL.");

    // ---- Event platform -----------------------------------------------------------

    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "atlas.events.published", unit: "{event}", description: "Integration events published to Kafka.");

    public static readonly Counter<long> EventsConsumed = Meter.CreateCounter<long>(
        "atlas.events.consumed", unit: "{event}", description: "Integration events successfully processed by a consumer.");

    public static readonly Counter<long> EventsDeadLettered = Meter.CreateCounter<long>(
        "atlas.events.dead_lettered", unit: "{event}", description: "Integration events routed to the dead-letter store after exhausting retries.");

    public static readonly Counter<long> EventRetries = Meter.CreateCounter<long>(
        "atlas.events.retries", unit: "{retry}", description: "In-flight event processing retries performed before dead-lettering.");

    public static readonly Counter<long> OutboxMessagesAbandoned = Meter.CreateCounter<long>(
        "atlas.outbox.abandoned", unit: "{message}", description: "Outbox messages abandoned after exhausting their retry budget (kept in the table for inspection, never retried automatically).");

    // ---- Reliability --------------------------------------------------------------

    public static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>(
        "atlas.ratelimit.rejections", unit: "{request}", description: "Requests rejected with HTTP 429 by the distributed rate limiter.");

    public static readonly Counter<long> RateLimitStoreFailures = Meter.CreateCounter<long>(
        "atlas.ratelimit.store.failures", unit: "{request}", description: "Rate-limit evaluations that failed because the Redis store was unreachable (requests failed open by design).");

    public static readonly Counter<long> CircuitBreakerStateChanges = Meter.CreateCounter<long>(
        "atlas.circuitbreaker.state.changes", unit: "{transition}", description: "Circuit breaker state transitions (Closed -> Open -> HalfOpen -> Closed).");

    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "atlas.cache.hits", unit: "{lookup}", description: "Cache lookups served from Redis.");

    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "atlas.cache.misses", unit: "{lookup}", description: "Cache lookups that had to fall through to the source of truth.");

    // ---- AI operations ------------------------------------------------------------

    public static readonly Counter<long> AiToolInvocations = Meter.CreateCounter<long>(
        "atlas.ai.tool.invocations", unit: "{invocation}", description: "AI operations tool invocations (read and action tools), tagged by tool name.");

    public static readonly Counter<long> AiAnswersWithoutEvidence = Meter.CreateCounter<long>(
        "atlas.ai.answers.insufficient_evidence", unit: "{answer}", description: "AI answers refused for lack of evidence ('Insufficient evidence.').");
}
