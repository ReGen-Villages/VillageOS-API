using System.Text.Json.Serialization;

namespace vos.Service.Intake.Models;

/// <summary>The document <c>POST /api/model/fragment</c> reads, applied as one upsert.</summary>
public sealed record ModelFragment(
    string Name,
    IReadOnlyList<FragmentThing> Things,
    IReadOnlyList<FragmentRelationship> Relationships);

public sealed record FragmentThing(Guid Id, string Name, IReadOnlyDictionary<string, TypedValue> Properties);

/// <summary>An edge carries no identifier: the endpoint keys an edge on subject, predicate and target, so a
/// re-posted submission finds its own edges rather than adding second copies of them.</summary>
public sealed record FragmentRelationship(string Name, Guid Subject, Guid Predicate, Guid Target);

/// <summary>A property value with the type it is written as.</summary>
public sealed record TypedValue
{
    [JsonPropertyName("typeInfo")]
    public required string TypeInfo { get; init; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Value { get; init; }

    public static TypedValue Written(string typeInfo, object value) => new() { TypeInfo = typeInfo, Value = value };
}

public static class VosTypeNames
{
    public const string Double = "vos.Double";
    public const string LongInteger = "vos.LongInteger";
    public const string Boolean = "vos.Boolean";
    public const string String = "vos.String";
    public const string GeoJson = "vos.GeoJson";
}
