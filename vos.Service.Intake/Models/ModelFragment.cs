using System.Text.Json.Serialization;

namespace vos.Service.Intake.Models;

/// <summary>A partial-model fragment as <c>POST /api/model/fragment</c> reads it: Things, the relationships
/// between them, and the values they carry, applied as one upsert.</summary>
public sealed record ModelFragment(
    string Name,
    IReadOnlyList<FragmentThing> Things,
    IReadOnlyList<FragmentRelationship> Relationships);

public sealed record FragmentThing(Guid Id, string Name, IReadOnlyDictionary<string, TypedValue> Properties);

/// <summary>An edge carries no identifier: the endpoint keys an edge on subject, predicate and target, so a
/// re-posted submission finds its own edges rather than adding second copies of them.</summary>
public sealed record FragmentRelationship(string Name, Guid Subject, Guid Predicate, Guid Target);

/// <summary>A property value with the type it is written as. A <see cref="Value"/> of null is omitted from
/// the document entirely, which declares the name and its type without asserting a value — how a computed
/// output waits for whatever computes it instead of holding a zero nothing produced.</summary>
public sealed record TypedValue
{
    [JsonPropertyName("typeInfo")]
    public required string TypeInfo { get; init; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Value { get; init; }

    public static TypedValue Written(string typeInfo, object value) => new() { TypeInfo = typeInfo, Value = value };

    public static TypedValue Declared(string typeInfo) => new() { TypeInfo = typeInfo };
}

public static class VosTypeNames
{
    public const string Double = "vos.Double";
    public const string LongInteger = "vos.LongInteger";
    public const string Boolean = "vos.Boolean";
    public const string String = "vos.String";
}
