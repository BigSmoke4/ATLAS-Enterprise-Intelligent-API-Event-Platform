using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas.Web.Json;

/// <summary>
/// Single serializer configuration for the JSON islands Razor emits for the
/// browser modules. Uses web defaults (camelCase) + string enums so the
/// JavaScript contracts match the REST APIs exactly — one shape, one set of
/// property names, one enum representation.
/// </summary>
public static class WebJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
