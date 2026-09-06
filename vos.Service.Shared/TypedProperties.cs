using System.Text.Json;

namespace vos.Service.Shared;

/// <summary>
/// Property values, written the way the broker takes them.
///
/// A Thing is created with <c>{"status": {"typeInfo": "vos.String", "value": "running"}}</c> and never
/// with <c>{"status": "running"}</c>. The create route reads each property through the same converter
/// the property routes use, and that converter refuses a value with no type on it — answering 400 with
/// no body to say which property was wrong. Three services wrote the bare shape (#6929, #6930).
/// </summary>
public static class TypedProperties
{
    private const string String = "vos.String";
    private const string Boolean = "vos.Boolean";
    private const string Double = "vos.Double";

    /// <summary>Every type name this can send. The broker serves the names it accepts at
    /// <c>/api/properties/types</c>, so a test can hold this against them rather than against a
    /// second list somebody keeps by hand.</summary>
    public static readonly IReadOnlyList<string> TypeNames = [String, Boolean, Double];

    /// <summary>The properties of a Thing being created, each in its envelope. Null stays null: a
    /// create carrying no properties is a create the broker takes as it is.</summary>
    public static Dictionary<string, object>? Typed<T>(IReadOnlyDictionary<string, T>? properties) =>
        properties?.ToDictionary(
            property => property.Key,
            property => Typed(property.Value));

    /// <summary>One value in its envelope.</summary>
    public static object Typed(object? value) => new { typeInfo = TypeNameFor(value), value };

    /// <summary>
    /// What the broker should hold the value as.
    ///
    /// A number is always a double, never an integer type, even when the value that arrived happens to
    /// have no fractional part. The first write decides the property's type and every later write is
    /// converted to it, so a reading of 3 recorded as an integer is a property that cannot hold the
    /// 3.5 that follows it.
    ///
    /// Anything with structure — an object, an array — is a string holding its JSON. The broker reads a
    /// property of no other type that way, and a caller that sent structure said something the model
    /// has no other place for.
    /// </summary>
    public static string TypeNameFor(object? value) => value switch
    {
        null => String,
        bool => Boolean,
        JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } => Boolean,
        JsonElement { ValueKind: JsonValueKind.Number } => Double,
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
            => Double,
        _ => String,
    };
}
