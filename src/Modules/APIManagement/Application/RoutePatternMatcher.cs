using System.Text.RegularExpressions;

namespace Atlas.Modules.APIManagement.Application;

/// <summary>
/// Matches a request path against a stored route template such as
/// <c>/api/v1/orders/{id}</c>. Used by the route policy provider so a route
/// registered once is found for every concrete request path, and so telemetry
/// can be attributed to the right service.
///
/// Regexes are compiled once per pattern and carry a match timeout so a
/// pathological pattern can never block a request thread (no user-supplied
/// regex is ever executed — patterns come from route templates created through
/// the validated API catalog).
/// </summary>
internal static class RoutePatternMatcher
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string pattern, string path)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(path)) return false;
        if (string.Equals(pattern, path, StringComparison.OrdinalIgnoreCase)) return true;
        if (!pattern.Contains('{', StringComparison.Ordinal)) return false;

        var regex = Cache.GetOrAdd(pattern, BuildRegex);
        return regex.IsMatch(path);
    }

    private static Regex BuildRegex(string pattern)
    {
        var literal = Regex.Escape(pattern).Replace("\\{", "{").Replace("\\}", "}", StringComparison.Ordinal);
        var expression = Regex.Replace(literal, @"\{[^/{}]+\}", "[^/]+");
        return new Regex("^" + expression + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }
}
