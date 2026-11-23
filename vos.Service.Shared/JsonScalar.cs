using System.Text.Json;

namespace vos.Service.Shared;

public static class JsonScalar
{
    // A whole number is read as a long so a count written back stays a whole number.
    public static bool TryRead(JsonElement element, out object? value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var whole) => whole,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
        return element.ValueKind is JsonValueKind.String or JsonValueKind.Number
            or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
    }
}
