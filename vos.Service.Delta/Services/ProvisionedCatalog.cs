namespace vos.Service.Delta.Services;

/// <summary>The endpoint-template catalog as it stands in one model: the `is` predicate a registration
/// is wired with, and the Thing each template was provisioned as. A template the run could not create
/// is absent rather than present-and-empty, so a registration naming it is refused instead of wired to
/// nothing.</summary>
public sealed record ProvisionedCatalog(Guid IsPredicateId, IReadOnlyDictionary<string, Guid> TemplateIdsByName);
