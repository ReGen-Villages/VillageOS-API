namespace vos.Service.Intake.Models;

/// <summary>The fragment a submission becomes, with the identifiers it minted and the reference the
/// submitter is answered with. The identifiers stay inside this service: the route is anonymous, so what
/// the model called the Things is not a stranger's to be told, and the reference is what a reviewer can
/// look the submission up under.</summary>
public sealed record ComposedSubmission(
    ModelFragment Fragment, string Reference, Guid SiteId, Guid StudyId, Guid? ParcelId);

/// <summary>A predicate the fragment relates Things with. <see cref="Minted"/> says the model does not hold
/// one under this name yet, so the fragment carries it.</summary>
public sealed record PredicateIdentity(string Name, Guid Id, bool Minted);

/// <summary>The predicates a submission may relate its Things with. These field names are matched to the
/// <c>…PredicateName</c> constants on <see cref="vos.Service.Intake.Services.SubmissionFragmentComposer"/> by a
/// tool that reads both files as text; read the remarks there before renaming one.</summary>
public sealed record ResolvedPredicates(
    PredicateIdentity Studies, PredicateIdentity Has, PredicateIdentity Is, PredicateIdentity Proposes);

/// <summary>The archetypes a submission's Things point at, as the model that will hold them names them. A
/// submission carries no property declarations of its own for anything an archetype already declares: it
/// relates its Things and inherits the rest.</summary>
public sealed record ResolvedArchetypes(
    Guid Site, Guid SiteStudy, Guid Parcel, Guid Project, Guid Contact, Guid ProgrammeAllocation,
    Guid HazardAssessment, Guid DataSource, Guid Submission);

/// <summary>One term a model declares, under the name it declared it with.</summary>
public sealed record DeclaredTerm(string Name, Guid Id);

/// <summary>A vocabulary the model holds as Things: the terms, and the predicate an edge to one of them is
/// written with. Both are read from the model rather than held here, so a project that adds a term or
/// renames the predicate changes the model and nothing else.</summary>
public sealed record DeclaredTerms(DeclaredTerm Predicate, IReadOnlyList<DeclaredTerm> Terms);

/// <summary>Where a site sits: the Place every other Place nests under, and the predicate that edge is
/// written with. Not a vocabulary — nothing is resolved against it, because a submission names no Place.
/// A source covering the root covers every site, so this one edge is what lets a covering source be
/// selected at all.</summary>
public sealed record DeclaredPlace(DeclaredTerm Predicate, DeclaredTerm Root);

/// <summary>The vocabularies a submission's words are resolved against before it becomes a fragment, and
/// the Place its site is related to.</summary>
public sealed record DeclaredVocabulary(
    DeclaredTerms AllocationCategories, DeclaredTerms BoundarySources, DeclaredTerms HazardTypes,
    DeclaredPlace PlaceNesting);
