using System.Text.Json;

namespace vos.ManagedMicroservice.Delta.Helpers;

/// <summary>
/// Pure helpers for normalizing values that arrive as <c>object?</c> inside JSON-deserialized
/// <c>IDictionary&lt;string, object&gt;</c> bodies (e.g. <c>RegisterEndpointRequest.Properties</c>).
/// Extracted from Program.cs under Feature #5433 / Task #5436 so they are unit-testable with
/// semantic value-out assertions instead of being exercised only through WebApplicationFactory.
/// </summary>
public static class JsonValueCoercion
{
    /// <summary>
    /// Convert an opaque <c>object?</c> (typically from a JSON-deserialized dictionary) into
    /// its string representation. <see cref="JsonElement"/> values are unwrapped according to
    /// their <see cref="JsonValueKind"/>; non-element values fall through to <c>ToString()</c>.
    /// </summary>
    public static string? CoerceToString(object? value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        return value?.ToString();
    }

    /// <summary>
    /// Find a property in <paramref name="properties"/> by name: exact match first, then a
    /// case-insensitive fallback scan. Returns <c>true</c> when found.
    /// </summary>
    public static bool TryGetPropertyValue(IDictionary<string, object> properties, string name, out object? value)
    {
        if (properties.TryGetValue(name, out value))
            return true;

        foreach (var entry in properties)
        {
            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Convenience combination of <see cref="TryGetPropertyValue"/> and
    /// <see cref="CoerceToString"/>: finds the property (case-insensitive fallback) and
    /// converts the value to a string. Returns <c>false</c> when the property is missing.
    /// </summary>
    public static bool TryGetStringProperty(IDictionary<string, object> properties, string name, out string? value)
    {
        value = null;
        if (!TryGetPropertyValue(properties, name, out var raw))
            return false;

        value = CoerceToString(raw);
        return true;
    }
}
