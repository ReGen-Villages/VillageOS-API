using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

/// <summary>
/// The shape a submission has to land in: <c>SiteStudy -studies-&gt; Site</c>, the site carrying what is true
/// of the land and the study carrying what an analysis makes of it. It is the shape an imported building
/// model already produces, so no reader has to ask where a site's facts came from, and a study is found by
/// its flag rather than by its source.
/// </summary>
public static class SubmissionFragmentComposer
{
    public const string SiteStudyFlag = "__IsSiteStudy";
    public const string StudiesPredicateName = "studies";
    public const string HasPredicateName = "has";
    public const string IsPredicateName = "is";

    public const string SiteArchetypeName = "Site";
    public const string SiteStudyArchetypeName = "SiteStudy";
    public const string ParcelArchetypeName = "Parcel";

    private static readonly IReadOnlyList<string> BoundarySources =
        ["drawn-by-hand", "imported-from-file", "generated-from-stated-area"];

    public static ComposedSubmission Compose(
        Submission submission, ResolvedPredicates predicates, ResolvedArchetypes archetypes)
    {
        var submissionId = Required(submission.SubmissionId, "submissionId",
            "every identifier derives from it, so a submission posted twice without one would build a second site");
        var site = submission.Site
            ?? throw new SubmissionError("'site' is missing: a submission is a site and what is known about it.");
        var siteName = Required(site.Name, "site.name", "a Thing is created under a name");

        var siteThing = new NamedThing(StableIdentity.Derive(submissionId, "site"), siteName);
        var studyThing = new NamedThing(StableIdentity.Derive(submissionId, "study"), $"{siteName} Site Study");

        var things = new List<FragmentThing>
        {
            new(siteThing.Id, siteThing.Name, SiteProperties(site)),
            new(studyThing.Id, studyThing.Name, StudyProperties()),
        };
        var relationships = new List<FragmentRelationship>();
        var mintedPredicates = new Dictionary<Guid, FragmentThing>();

        void Relate(NamedThing subject, PredicateIdentity predicate, NamedThing target)
        {
            if (predicate.Minted)
                mintedPredicates[predicate.Id] = new FragmentThing(predicate.Id, predicate.Name, new Dictionary<string, TypedValue>());
            relationships.Add(new FragmentRelationship(
                $"{subject.Name} {predicate.Name} {target.Name}", subject.Id, predicate.Id, target.Id));
        }

        // Everything an archetype declares is inherited through this edge, so a correction is an edit to
        // the model rather than a redeployment of this service.
        void BeArchetype(NamedThing thing, Guid archetype, string archetypeName) =>
            Relate(thing, predicates.Is, new NamedThing(archetype, archetypeName));

        Relate(studyThing, predicates.Studies, siteThing);
        BeArchetype(siteThing, archetypes.Site, SiteArchetypeName);
        BeArchetype(studyThing, archetypes.SiteStudy, SiteStudyArchetypeName);

        Guid? parcelId = null;
        if (submission.Parcel is { } parcel)
        {
            var parcelThing = new NamedThing(StableIdentity.Derive(submissionId, "parcel"), $"{siteName} Parcel-01");
            parcelId = parcelThing.Id;
            things.Add(new FragmentThing(parcelThing.Id, parcelThing.Name, ParcelProperties(parcel)));
            Relate(siteThing, predicates.Has, parcelThing);
            BeArchetype(parcelThing, archetypes.Parcel, ParcelArchetypeName);
        }

        var fragment = new ModelFragment($"{siteName} submission", [.. mintedPredicates.Values, .. things], relationships);
        return new ComposedSubmission(fragment, siteThing.Id, studyThing.Id, parcelId);
    }

    private readonly record struct NamedThing(Guid Id, string Name);

    private static Dictionary<string, TypedValue> SiteProperties(SubmittedSite site)
    {
        var properties = new Dictionary<string, TypedValue>();
        Write(properties, "latitude", VosTypeNames.Double, site.Latitude);
        Write(properties, "longitude", VosTypeNames.Double, site.Longitude);
        Write(properties, "statedAreaHectares", VosTypeNames.Double, site.StatedAreaHectares);
        Write(properties, "population", VosTypeNames.LongInteger, site.Population);
        Write(properties, "householdSize", VosTypeNames.Double, site.HouseholdSize);
        return properties;
    }

    // Only the flag readers locate a study by. Every computed output is declared on the archetype, and a
    // declaration here could only disagree with it.
    private static Dictionary<string, TypedValue> StudyProperties() => new()
    {
        [SiteStudyFlag] = TypedValue.Written(VosTypeNames.Boolean, true),
    };

    private static Dictionary<string, TypedValue> ParcelProperties(SubmittedParcel parcel)
    {
        var boundary = parcel.Boundary
            ?? throw new SubmissionError("'parcel.boundary' is missing: a parcel is the boundary it encloses. "
                                       + "Leave 'parcel' out until one has been drawn.");
        if (boundary.Count < 3)
            throw new SubmissionError(
                $"'parcel.boundary' has {boundary.Count} corner(s): a boundary needs at least three.");

        var source = Required(parcel.BoundarySource, "parcel.boundarySource",
            "a square generated from a stated area is not evidence and must not read as a surveyed boundary");
        if (!BoundarySources.Contains(source))
            throw new SubmissionError(
                $"'parcel.boundarySource' is '{source}': it must be one of {string.Join(", ", BoundarySources)}.");

        return new Dictionary<string, TypedValue>
        {
            ["measuredAreaHectares"] = TypedValue.Written(VosTypeNames.Double, BoundaryGeometry.MeasureHectares(boundary)),
            ["boundary"] = TypedValue.Written(VosTypeNames.GeoJson, BoundaryGeometry.ToGeoJson(boundary)),
            ["boundarySource"] = TypedValue.Written(VosTypeNames.String, source),
        };
    }

    private static void Write(IDictionary<string, TypedValue> properties, string name, string typeInfo, object? value)
    {
        if (value is not null)
            properties[name] = TypedValue.Written(typeInfo, value);
    }

    private static string Required(string? value, string field, string why) =>
        string.IsNullOrWhiteSpace(value) ? throw new SubmissionError($"'{field}' is missing: {why}.") : value;
}
