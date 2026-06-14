namespace vos.ManagedMicroservice.Delta.Models;

/// <summary>
/// A Delta endpoint-template seed document — a simplified, hand-authored mirror of a VosModel
/// fragment. It carries the template Things and the relationships among them. Template inheritance
/// is expressed the model-native way, as <c>is</c> relationships in <see cref="Relationships"/>
/// (never a scalar field on a thing), matching how Mycelium and CLI serialize a model. References
/// are by template name; Delta resolves them to mycelium GUIDs at realization time.
///
/// Introduced under Feature #5465 / Task #5466.
/// </summary>
public sealed class EndpointSeedModel
{
    /// <summary>Optional document name (the model name); not part of the template graph.</summary>
    public string? Name { get; set; }

    /// <summary>The template things, each a name + flat property bag.</summary>
    public List<RegisterEndpointRequest> Things { get; set; } = new();

    /// <summary>Relationships among the things; <c>is</c> rows define the inheritance hierarchy.</summary>
    public List<SeedRelationship> Relationships { get; set; } = new();
}

/// <summary>A name-keyed relationship row in an <see cref="EndpointSeedModel"/> (mirrors a model relationship).</summary>
public sealed class SeedRelationship
{
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}
