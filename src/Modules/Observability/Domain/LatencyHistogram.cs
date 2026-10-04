namespace Atlas.Modules.Observability.Domain;

/// <summary>
/// Fixed-bucket latency histogram: the storage half of ATLAS's percentile
/// story. Percentiles are computed from buckets (never from an average, and
/// never invented) so a single row per minute per route is enough to answer
/// P50/P95/P99 without storing one row per request.
///
/// Percentile semantics are documented and deliberately conservative: the
/// returned value is the UPPER boundary of the bucket that contains the
/// requested percentile — i.e. ATLAS never reports a percentile lower than
/// reality. The overflow bucket reports its lower boundary and therefore
/// means "at least this".
/// </summary>
public static class LatencyHistogram
{
    public static readonly double[] BucketUpperBoundsMs =
    {
        5, 10, 25, 50, 75, 100, 150, 200, 300, 500, 750, 1000, 2000, 5000, 10000
    };

    /// <summary>Number of buckets = upper bounds + the overflow ("&gt; 10s") bucket.</summary>
    public static int BucketCount => BucketUpperBoundsMs.Length + 1;

    public static long[] CreateEmptyBucketCounts() => new long[BucketCount];

    public static int BucketIndex(double durationMs)
    {
        if (durationMs < 0) durationMs = 0;
        for (var i = 0; i < BucketUpperBoundsMs.Length; i++)
        {
            if (durationMs <= BucketUpperBoundsMs[i]) return i;
        }
        return BucketUpperBoundsMs.Length; // overflow bucket
    }

    /// <summary>
    /// Resolves a percentile over bucket counts.
    /// </summary>
    /// <returns>
    /// The bucket's upper boundary in milliseconds, or null when the histogram
    /// holds no observations (no data must never become a fabricated 0 ms).
    /// </returns>
    public static double? PercentileMs(IReadOnlyList<long> bucketCounts, double percentile)
    {
        if (bucketCounts is null || bucketCounts.Count == 0) return null;

        long total = 0;
        foreach (var count in bucketCounts) total += count;
        if (total <= 0) return null;

        if (percentile <= 0) percentile = 1;
        if (percentile > 100) percentile = 100;

        // Ceiling-based rank keeps the result on the safe (higher) side for
        // small samples, e.g. P95 of 10 observations is the 10th observation.
        var target = (long)Math.Ceiling(percentile / 100d * total);
        if (target < 1) target = 1;

        long cumulative = 0;
        for (var bucket = 0; bucket < bucketCounts.Count; bucket++)
        {
            cumulative += bucketCounts[bucket];
            if (cumulative >= target)
            {
                return bucket < BucketUpperBoundsMs.Length
                    ? BucketUpperBoundsMs[bucket]
                    : BucketUpperBoundsMs[^1]; // overflow: "at least 10000 ms"
            }
        }

        return BucketUpperBoundsMs[^1];
    }

    public static void Add(IReadOnlyList<long> source, long[] target)
    {
        var limit = Math.Min(source.Count, target.Length);
        for (var i = 0; i < limit; i++) target[i] += source[i];
    }

    public static long Total(IReadOnlyList<long> bucketCounts)
    {
        long total = 0;
        foreach (var count in bucketCounts) total += count;
        return total;
    }
}
