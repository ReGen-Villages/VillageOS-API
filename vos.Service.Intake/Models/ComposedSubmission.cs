namespace vos.Service.Intake.Models;

// The fragment a submission becomes, with the identifiers it minted and the reference the
// submitter is answered with. The identifiers stay inside this service: the route is anonymous, so what
// the model called the Things is not a stranger's to be told, and the reference is what a reviewer can
// look the submission up under.
public sealed record ComposedSubmission(
    ModelFragment Fragment, string Reference, Guid SiteId, Guid StudyId, Guid? ParcelId);

// A predicate the fragment relates Things with. Minted says the model does not hold
// one under this name yet, so the fragment carries it.
public sealed record PredicateIdentity(string Name, Guid Id, bool Minted);

// The predicates a submission may relate its Things with. These field names are matched to the
// …PredicateName constants on vos.Service.Intake.Services.SubmissionFragmentComposer by a
// tool that reads both files as text; read the remarks there before renaming one.
public sealed record ResolvedPredicates(
    PredicateIdentity Studies, PredicateIdentity Has, PredicateIdentity Is, PredicateIdentity Proposes,
    PredicateIdentity ServedAfter);

// The archetypes a submission's Things point at, as the model that will hold them names them. A
// submission carries no property declarations of its own for anything an archetype already declares: it
// relates its Things and inherits the rest.
public sealed record ResolvedArchetypes(
    Guid Site, Guid SiteStudy, Guid Parcel, Guid Project, Guid Contact, Guid ProgrammeAllocation,
    Guid HazardAssessment, Guid SubmittedSource, Guid Submission);

public sealed record DeclaredTerm(string Name, Guid Id);

// One demand on the water a site harvests, and where in the queue it stands. An archetype rather
// than a term: a study holds a demand of its own under each of these, and nothing in a submission names
// one — they are minted for every study whatever was submitted.
public sealed record DeclaredDemand(string Name, Guid Id, long ServingOrder);

// A vocabulary the model holds as Things: the terms, and the predicate a relationship to one of them is
// written with. Both are read from the model rather than held here, so a project that adds a term or
// renames the predicate changes the model and nothing else.
public sealed record DeclaredTerms(DeclaredTerm Predicate, IReadOnlyList<DeclaredTerm> Terms);

// Where a site sits: the Place every other Place nests under, and the predicate that relationship is
// written with. Not a vocabulary — nothing is resolved against it, because a submission names no Place.
// A source covering the root covers every site, so this one relationship is what lets a covering source be
// selected at all.
public sealed record DeclaredPlace(DeclaredTerm Predicate, DeclaredTerm Root);

// The vocabularies a submission's words are resolved against before it becomes a fragment, and
// the Place its site is related to.
public sealed record DeclaredVocabulary(
    DeclaredTerms AllocationCategories, DeclaredTerms BoundarySources, DeclaredTerms HazardTypes,
    DeclaredTerms HazardLevels, DeclaredPlace PlaceNesting, IReadOnlyList<DeclaredDemand> WaterDemands);
