using Atlas.Modules.EventPlatform.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.EventPlatform.Application;

/// <summary>Sortable fields for GET /api/v1/events/dead-letters — default ordering: most recent failure first.</summary>
public static class DeadLetterSorting
{
    public static readonly SortSpec<DeadLetterEvent> DeadLetters = SortSpec<DeadLetterEvent>.Create(
        defaultField: "lastFailedAtUtc",
        defaultOrdering: q => q.OrderByDescending(d => d.LastFailedAtUtc),
        ("lastFailedAtUtc", (desc, q) => desc ? q.OrderByDescending(d => d.LastFailedAtUtc) : q.OrderBy(d => d.LastFailedAtUtc)),
        ("firstFailedAtUtc", (desc, q) => desc ? q.OrderByDescending(d => d.FirstFailedAtUtc) : q.OrderBy(d => d.FirstFailedAtUtc)),
        ("retryCount", (desc, q) => desc ? q.OrderByDescending(d => d.RetryCount) : q.OrderBy(d => d.RetryCount)),
        ("eventType", (desc, q) => desc ? q.OrderByDescending(d => d.EventType) : q.OrderBy(d => d.EventType)),
        ("originalTopic", (desc, q) => desc ? q.OrderByDescending(d => d.OriginalTopic) : q.OrderBy(d => d.OriginalTopic)));
}
