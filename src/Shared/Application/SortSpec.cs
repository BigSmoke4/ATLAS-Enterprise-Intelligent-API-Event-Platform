namespace Atlas.Shared.Application;

/// <summary>Direction accepted by the list endpoints' <c>sortDirection</c> query parameter.</summary>
public enum SortDirection
{
    Ascending = 0,
    Descending = 1
}

/// <summary>Parses <c>sortDirection</c> ("asc"/"ascending"/"desc"/"descending", case-insensitive, empty = ascending).</summary>
public static class SortDirections
{
    public static bool TryParse(string? value, out SortDirection direction)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "":
                direction = SortDirection.Ascending;
                return true;
            case "asc" or "ascending":
                direction = SortDirection.Ascending;
                return true;
            case "desc" or "descending":
                direction = SortDirection.Descending;
                return true;
            default:
                direction = SortDirection.Ascending;
                return false;
        }
    }
}

/// <summary>
/// The whitelist of fields one list resource may be sorted by, plus the fixed
/// ordering that applies when the caller does not ask for one.
///
/// The field map is applied at query level (before Skip/Take) and every entry
/// is a real LINQ ordering rather than a property name resolved at runtime, so
/// an unknown field can never reach the database as a string, and the sort is
/// translated to SQL by EF Core exactly like a hand-written OrderBy.
///
/// Unknown fields are rejected by the controllers before this type is called —
/// see <c>SortQuery.TryResolve</c> in Atlas.Web, which turns them into a 400
/// ProblemDetails (INVALID_SORT_FIELD) instead of silently returning unsorted
/// data.
/// </summary>
public sealed class SortSpec<T>
{
    private readonly Dictionary<string, Func<bool, IQueryable<T>, IOrderedQueryable<T>>> _fields;
    private readonly Func<IQueryable<T>, IOrderedQueryable<T>> _defaultOrdering;
    private readonly string[] _fieldNames;

    private SortSpec(string defaultField, Func<IQueryable<T>, IOrderedQueryable<T>> defaultOrdering,
        Dictionary<string, Func<bool, IQueryable<T>, IOrderedQueryable<T>>> fields)
    {
        DefaultField = defaultField;
        _defaultOrdering = defaultOrdering;
        _fields = fields;
        _fieldNames = fields.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Field whose ordering reproduces the endpoint's default ordering — documented in docs/api.md.</summary>
    public string DefaultField { get; }

    /// <summary>Accepted <c>sortBy</c> values, alphabetically; also what the 400 response lists back to the caller.</summary>
    public IReadOnlyList<string> Fields => _fieldNames;

    /// <param name="defaultField">Must be one of <paramref name="fields"/>; the documented default sort.</param>
    /// <param name="defaultOrdering">Fixed ordering applied when no <c>sortBy</c> is supplied (may use ThenBy).</param>
    /// <param name="fields">
    /// Whitelisted fields. The bool argument is "descending"; each entry must build a real LINQ ordering so
    /// EF Core can translate it.
    /// </param>
    public static SortSpec<T> Create(string defaultField, Func<IQueryable<T>, IOrderedQueryable<T>> defaultOrdering,
        params (string Field, Func<bool, IQueryable<T>, IOrderedQueryable<T>> Order)[] fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultField);
        ArgumentNullException.ThrowIfNull(defaultOrdering);

        var map = new Dictionary<string, Func<bool, IQueryable<T>, IOrderedQueryable<T>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, order) in fields)
        {
            if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("Sort field name is required.", nameof(fields));
            if (order is null) throw new ArgumentException($"Sort field '{field}' has no ordering.", nameof(fields));
            map[field.Trim()] = order;
        }

        if (!map.ContainsKey(defaultField))
            throw new ArgumentException($"Default sort field '{defaultField}' must be one of the whitelisted fields.", nameof(defaultField));

        return new SortSpec<T>(defaultField, defaultOrdering, map);
    }

    /// <summary>True when the caller asked for nothing (default ordering applies) or asked for a whitelisted field.</summary>
    public bool IsKnown(string? field) => string.IsNullOrWhiteSpace(field) || _fields.ContainsKey(field.Trim());

    /// <summary>
    /// Orders the query. Callers must validate with <see cref="IsKnown"/> first — reaching this with an
    /// unknown field means a controller lost its validation, which is a bug, not a user error.
    /// </summary>
    public IQueryable<T> Apply(IQueryable<T> query, string? field, SortDirection direction = SortDirection.Ascending)
    {
        if (string.IsNullOrWhiteSpace(field)) return _defaultOrdering(query);

        if (!_fields.TryGetValue(field.Trim(), out var order))
            throw new ArgumentOutOfRangeException(nameof(field), field,
                $"Unknown sort field. Allowed values: {string.Join(", ", _fieldNames)}.");

        return order(direction == SortDirection.Descending, query);
    }
}
