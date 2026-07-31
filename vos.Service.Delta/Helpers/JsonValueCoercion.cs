using System.Text.Json;

namespace vos.Service.Delta.Helpers;

public static class JsonValueCoercion
{
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

    public static bool TryGetStringProperty(IDictionary<string, object> properties, string name, out string? value)
    {
        value = null;
        if (!TryGetPropertyValue(properties, name, out var raw))
            return false;

        value = CoerceToString(raw);
        return true;
    }
}
