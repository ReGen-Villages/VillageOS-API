namespace vos.Service.Intake.Models;

/// <summary>The fragment a submission becomes, with the identifiers it minted — the caller gets them back so
/// a wizard can carry on editing the same site rather than starting another one.</summary>
public sealed record ComposedSubmission(ModelFragment Fragment, Guid SiteId, Guid StudyId, Guid? ParcelId);

/// <summary>A predicate the fragment relates Things with. <see cref="Minted"/> says the model does not hold
/// one under this name yet, so the fragment carries it.</summary>
public sealed record PredicateIdentity(string Name, Guid Id, bool Minted);

public sealed record ResolvedPredicates(PredicateIdentity Studies, PredicateIdentity Has);
