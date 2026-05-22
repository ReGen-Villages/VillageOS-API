using System.Text.Json;

namespace vos.ManagedMicroservice.Metabolism.Helpers;

/// <summary>
/// Unwraps a <see cref="JsonElement"/> to its native CLR type. SignalR (and other JSON
/// transports) hand the simulation engine values as <c>object?</c> that are actually
/// <see cref="JsonElement"/> instances; the engine wants <c>int</c>/<c>long</c>/<c>decimal</c>/
/// <c>string</c>/<c>bool</c>/<c>null</c> so property assignment compares cleanly.
///
/// Extracted from <c>Services/Metabolism.cs</c> under Feature #5433 / Task #5436 so the
/// <see cref="JsonValueKind"/> arms are unit-testable with semantic value-out assertions.
/// </summary>
public static class JsonValueUnwrapper
{
    /// <summary>
    /// If <paramref name="value"/> is a <see cref="JsonElement"/>, unwrap it to the
    /// narrowest native CLR type that fits its <see cref="JsonValueKind"/>. Numbers prefer
    /// <c>int</c>, then <c>long</c>, then <c>decimal</c>. Non-element values pass through
    /// unchanged.
    /// </summary>
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
