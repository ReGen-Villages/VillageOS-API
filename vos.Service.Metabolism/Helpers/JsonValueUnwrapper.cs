using System.Text.Json;

namespace vos.Service.Metabolism.Helpers;

// Unwraps a JsonElement to its native CLR type — JSON transports hand the engine
// values typed as object? that are really JsonElement, and the engine needs
// native types so property assignment compares cleanly.
public static class JsonValueUnwrapper
{
    public static object? Unwrap(object? value)
    {
        if (value is not JsonElement je) return value;
        return je.ValueKind switch
        {
            JsonValueKind.Number when je.TryGetInt32(out var i) => i,
            JsonValueKind.Number when je.TryGetInt64(out var l) => l,
            JsonValueKind.Number => je.GetDecimal(),
            JsonValueKind.String => je.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => je.GetRawText()
        };
    }
}
