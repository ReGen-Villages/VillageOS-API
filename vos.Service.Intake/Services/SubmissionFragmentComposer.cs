using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

/// <summary>
/// Turns a submission into the one fragment that puts it into the model.
///
/// The result is the shape an imported building model already produces — <c>SiteStudy -studies-&gt; Site</c>,
/// the study carrying what an analysis makes of the land and the site carrying what is true of it — so no
/// reader has to ask where a site's facts came from. A study is found by its flag, never by its source.
/// </summary>
public static class SubmissionFragmentComposer
{
    public const string SiteStudyFlag = "__IsSiteStudy";
    public const string StudiesPredicateName = "studies";
    public const string HasPredicateName = "has";

    private static readonly IReadOnlyList<string> BoundarySources =
        ["drawn-by-hand", "imported-from-file", "generated-from-stated-area"];

    // What the site analysis writes onto the study. Each is declared with its type and no value: a seeded
    // zero cannot be told from a real result, and a range reading one would report a verdict about an
    // analysis that never ran.
    private static readonly IReadOnlyDictionary<string, string> ComputedOutputs =
        new Dictionary<string, string> { ["energySelfSufficiencyPct"] = VosTypeNames.Double };

    public static ComposedSubmission Compose(Submission submission, ResolvedPredicates predicates)
    {
        var submissionId = Required(submission.SubmissionId, "submissionId",
            "every identifier derives from it, so a submission posted twice without one would build a second site");
        var site = submission.Site
            ?? throw new SubmissionError("'site' is missing: a submission is a site and what is known about it.");
        var siteName = Required(site.Name, "site.name", "a Thing is created under a name");

        var siteId = StableIdentity.Derive(submissionId, "site");
        var studyId = StableIdentity.Derive(submissionId, "study");
        var studyName = $"{siteName} Site Study";

        var things = new List<FragmentThing>
        {
            new(siteId, siteName, SiteProperties(site)),
            new(studyId, studyName, StudyProperties()),
        };
        var relationships = new List<FragmentRelationship>();
        var mintedPredicates = new Dictionary<Guid, FragmentThing>();

        void Relate(Guid subject, string subjectName, PredicateIdentity predicate, Guid target, string targetName)
        {
            if (predicate.Minted)
                mintedPredicates[predicate.Id] = new FragmentThing(predicate.Id, predicate.Name, new Dictionary<string, TypedValue>());
            relationships.Add(new FragmentRelationship(
                $"{subjectName} {predicate.Name} {targetName}", subject, predicate.Id, target));
        }

        Relate(studyId, studyName, predicates.Studies, siteId, siteName);

        Guid? parcelId = null;
        if (submission.Parcel is { } parcel)
        {
            parcelId = StableIdentity.Derive(submissionId, "parcel");
            var parcelName = $"{siteName} Parcel-01";
            things.Add(new FragmentThing(parcelId.Value, parcelName, ParcelProperties(parcel)));
            Relate(siteId, siteName, predicates.Has, parcelId.Value, parcelName);
        }

        var fragment = new ModelFragment($"{siteName} submission", [.. mintedPredicates.Values, .. things], relationships);
        return new ComposedSubmission(fragment, siteId, studyId, parcelId);
    }

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

    private static Dictionary<string, TypedValue> StudyProperties()
    {
        var properties = new Dictionary<string, TypedValue>
        {
            [SiteStudyFlag] = TypedValue.Written(VosTypeNames.Boolean, true),
        };
        foreach (var (name, typeInfo) in ComputedOutputs)
            properties[name] = TypedValue.Declared(typeInfo);
        return properties;
    }

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
            ["boundary"] = TypedValue.Written(VosTypeNames.String, BoundaryGeometry.ToGeoJson(boundary)),
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
