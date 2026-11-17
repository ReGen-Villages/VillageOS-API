using vos.Service.Intake.Models;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

public sealed record SiteLayerDeclarations(
    DeclaredTerm Feature, DeclaredTerms Kinds, DeclaredTerm SourcedFrom, DeclaredTerm Has);

public static class SiteLayerReader
{
    public const string GeometryProperty = "geometry";

    public static SubscriptionSelector Selector() => new()
    {
        Ids = RoleBindings.Identifiers(
            LandIntakeRoles.SiteLayerFeature, LandIntakeRoles.SiteLayerKind,
            LandIntakeRoles.OfLayerKind, LandIntakeRoles.SourcedFrom),
        Names =
        [
            SubmissionFragmentComposer.IsPredicateName, SubmissionFragmentComposer.HasPredicateName,
            RoleBindings.BindsPredicateName,
        ],
        BoundTypes = RoleBindings.Identifiers(LandIntakeRoles.SiteLayerKind),
        Traverse = [RoleBindings.FollowBindings],
        IncludeRelationships = true,
    };

    // Null where the model declares no site layer: a document removed from such a model has no features.
    public static SiteLayerDeclarations? Read(SnapshotDocument snapshot)
    {
        var feature = snapshot.Bound(LandIntakeRoles.SiteLayerFeature).FirstOrDefault();
        var kind = snapshot.Bound(LandIntakeRoles.SiteLayerKind).FirstOrDefault();
        var ofKind = snapshot.Bound(LandIntakeRoles.OfLayerKind).FirstOrDefault();
        var sourcedFrom = snapshot.Bound(LandIntakeRoles.SourcedFrom).FirstOrDefault();
        var has = snapshot.Things.FirstOrDefault(thing =>
            string.Equals(thing.Name, SubmissionFragmentComposer.HasPredicateName, StringComparison.Ordinal));
        if (feature is null || kind is null || ofKind is null || sourcedFrom is null || has is null) return null;

        return new SiteLayerDeclarations(
            Term(feature),
            new DeclaredTerms(Term(ofKind), DeclaredVocabularyReader.TermsUnder(snapshot, kind.Id)),
            Term(sourcedFrom),
            Term(has));
    }

    public static SubscriptionSelector FeaturesSelector(Guid documentId) => new()
    {
        Ids = [documentId],
        Traverse = [new TraverseRule { PredicateRole = LandIntakeRoles.SourcedFrom.Id, Direction = "incoming" }],
        IncludeRelationships = true,
    };

    // Only a Thing that `is` a feature, so removing a document never takes anything else that names it as a source.
    public static IReadOnlyList<Guid> FeaturesFrom(
        SnapshotDocument snapshot, Guid documentId, SiteLayerDeclarations declared, Guid isPredicate)
    {
        var features = snapshot.Relationships
            .Where(edge => edge.PredicateId == isPredicate && edge.TargetId == declared.Feature.Id)
            .Select(edge => edge.SubjectId)
            .ToHashSet();
        return [.. snapshot.Relationships
            .Where(edge => edge.PredicateId == declared.SourcedFrom.Id && edge.TargetId == documentId)
            .Select(edge => edge.SubjectId)
            .Where(features.Contains)
            .Distinct()];
    }

    private static DeclaredTerm Term(SnapshotThing thing) => new(thing.Name ?? string.Empty, thing.Id);
}
