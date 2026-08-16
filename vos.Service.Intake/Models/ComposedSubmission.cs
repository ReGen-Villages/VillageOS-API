namespace vos.Service.Intake.Models;

/// <summary>The fragment a submission becomes, with the identifiers it minted — the caller gets them back so
/// a wizard can carry on editing the same site rather than starting another one.</summary>
public sealed record ComposedSubmission(ModelFragment Fragment, Guid SiteId, Guid StudyId, Guid? ParcelId);

/// <summary>A predicate the fragment relates Things with. <see cref="Minted"/> says the model does not hold
/// one under this name yet, so the fragment carries it.</summary>
public sealed record PredicateIdentity(string Name, Guid Id, bool Minted);

/// <summary>The predicates a submission may relate its Things with. These field names are matched to the
/// <c>…PredicateName</c> constants on <see cref="vos.Service.Intake.Services.SubmissionFragmentComposer"/> by a
/// tool that reads both files as text; read the remarks there before renaming one.</summary>
public sealed record ResolvedPredicates(PredicateIdentity Studies, PredicateIdentity Has, PredicateIdentity Is);

/// <summary>The archetypes a submission's Things point at, as the model that will hold them names them. A
/// submission carries no property declarations of its own for anything an archetype already declares: it
/// relates its Things and inherits the rest.</summary>
public sealed record ResolvedArchetypes(
    Guid Site, Guid SiteStudy, Guid Parcel, Guid Project, Guid Contact, Guid ProgrammeAllocation,
    Guid HazardAssessment, Guid DataSource);
