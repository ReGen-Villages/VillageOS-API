using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

/// <summary>
/// The shape a submission has to land in: <c>SiteStudy -studies-&gt; Site</c>, the site carrying what is true
/// of the land and the study carrying what an analysis makes of it. It is the shape an imported building
/// model already produces, so no reader has to ask where a site's facts came from, and a study is found by
/// its flag rather than by its source.
/// </summary>
/// <remarks>
/// A tool in the VillageOS repository reads this file as text. It checks what a submission writes against the
/// archetypes that declare those properties, and this file is the only place it can learn that. What it takes
/// from the shape below: the property maps, by the names <c>SiteProperties</c>, <c>StudyProperties</c>,
/// <c>ParcelProperties</c>, <c>ProjectProperties</c>, <c>ContactProperties</c>, <c>AllocationProperties</c>,
/// <c>HazardProperties</c> and <c>DataSourceProperties</c>, one per Thing; each property, from a
/// <c>Write(properties, …)</c> call or a
/// <c>["name"] = TypedValue.…</c> entry; the archetypes a submission is composed against, from the
/// <c>…ArchetypeName</c> constants; and the predicates it may use, from the <c>…PredicateName</c> constants,
/// matched by name to the fields of <see cref="vos.Service.Intake.Models.ResolvedPredicates"/>.
/// <para>
/// Renaming any of them compiles and passes every test here, and the failures are not alike. A renamed
/// property map stops the model reference building. A renamed <c>…ArchetypeName</c> is quieter: that list is
/// the gate deciding whether a model is one this producer targets at all, so a shorter list weakens the gate
/// rather than tripping it, and the reference builds full of findings about a model the producer was never
/// pointed at. A map added here and not there is quieter still — it builds, it passes, and what that Thing
/// carries is never checked against the archetype declaring it.
/// </para>
/// <para>
/// Adding an <c>…ArchetypeName</c> tightens the gate rather than weakening it: the producer section reports
/// only on a model holding every archetype named here, so a model carrying some of them and not the rest
/// goes silent. That is the intended reading — a submission is composed against the whole set — but it means
/// a name added here narrows which models the reference says anything about.
/// </para>
/// </remarks>
public static class SubmissionFragmentComposer
{
    public const string SiteStudyFlag = "__IsSiteStudy";
    public const string StudiesPredicateName = "studies";
    public const string HasPredicateName = "has";
    public const string IsPredicateName = "is";

    public const string SiteArchetypeName = "Site";
    public const string SiteStudyArchetypeName = "SiteStudy";
    public const string ParcelArchetypeName = "Parcel";
    public const string ProjectArchetypeName = "Project";
    public const string ContactArchetypeName = "Contact";
    public const string ProgrammeAllocationArchetypeName = "ProgrammeAllocation";
    public const string HazardAssessmentArchetypeName = "HazardAssessment";
    public const string DataSourceArchetypeName = "DataSource";

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

        // The project holds the site rather than the other way round: a submission is one planner's
        // undertaking, and the site is what it is about.
        if (submission.Project is { } project)
        {
            var projectThing = new NamedThing(
                StableIdentity.Derive(submissionId, "project"),
                Required(project.Name, "project.name", "a Thing is created under a name"));
            things.Add(new FragmentThing(projectThing.Id, projectThing.Name, ProjectProperties(project)));
            Relate(projectThing, predicates.Has, siteThing);
            BeArchetype(projectThing, archetypes.Project, ProjectArchetypeName);

            if (submission.Contact is { } contact)
            {
                var contactThing = new NamedThing(
                    StableIdentity.Derive(submissionId, "contact"),
                    Required(contact.Name, "contact.name", "a Thing is created under a name"));
                things.Add(new FragmentThing(contactThing.Id, contactThing.Name, ContactProperties(contact)));
                Relate(projectThing, predicates.Has, contactThing);
                BeArchetype(contactThing, archetypes.Contact, ContactArchetypeName);
            }
        }
        else if (submission.Contact is not null)
        {
            throw new SubmissionError(
                "'contact' was given without 'project': a contact hangs off the project it can be asked about, "
                + "so there is nowhere to put one on its own.");
        }

        Guid? parcelId = null;
        if (submission.Parcel is { } parcel)
        {
            var parcelThing = new NamedThing(StableIdentity.Derive(submissionId, "parcel"), $"{siteName} Parcel-01");
            parcelId = parcelThing.Id;
            things.Add(new FragmentThing(parcelThing.Id, parcelThing.Name, ParcelProperties(parcel)));
            Relate(siteThing, predicates.Has, parcelThing);
            BeArchetype(parcelThing, archetypes.Parcel, ParcelArchetypeName);
        }

        var categoriesAlreadyGiven = new Dictionary<string, string>();
        foreach (var allocation in submission.Allocations ?? [])
        {
            var category = Required(allocation.Category, "allocation.category",
                "an allocation is a share of the land put to some named use").Trim();

            // Identity comes from the category rather than from a position in the list, so a wizard that
            // reorders them re-posts onto the same Things. Case and surrounding space are not part of what
            // the planner meant, so they are not part of what identifies it either.
            var key = category.ToLowerInvariant();
            if (categoriesAlreadyGiven.TryGetValue(key, out var alreadyGiven))
                throw new SubmissionError(
                    $"'allocations' gives '{alreadyGiven}' and '{category}' as separate shares of one "
                    + "category: they disagree about it and nothing here can say which was meant.");
            categoriesAlreadyGiven[key] = category;

            var allocationThing = new NamedThing(
                StableIdentity.Derive(submissionId, $"allocation:{key}"), $"{siteName} {category}");
            things.Add(new FragmentThing(allocationThing.Id, allocationThing.Name,
                AllocationProperties(allocation with { Category = category })));
            Relate(siteThing, predicates.Has, allocationThing);
            BeArchetype(allocationThing, archetypes.ProgrammeAllocation, ProgrammeAllocationArchetypeName);
        }

        // A source named by two hazards is one Thing both hang off, not one each. Identity comes from the
        // source's name for the same reason an allocation's comes from its category.
        var sourcesAlreadyMinted = new Dictionary<string, NamedThing>();
        var hazardsAlreadyGiven = new Dictionary<string, string>();
        foreach (var hazard in submission.Hazards ?? [])
        {
            var hazardType = Required(hazard.HazardType, "hazard.hazardType",
                "an assessment is about one named hazard").Trim();
            var typeKey = hazardType.ToLowerInvariant();
            if (hazardsAlreadyGiven.TryGetValue(typeKey, out var alreadyGiven))
                throw new SubmissionError(
                    $"'hazards' gives '{alreadyGiven}' and '{hazardType}' as separate assessments of one "
                    + "hazard: they disagree about it and nothing here can say which was meant.");
            hazardsAlreadyGiven[typeKey] = hazardType;

            var hazardThing = new NamedThing(
                StableIdentity.Derive(submissionId, $"hazard:{typeKey}"), $"{siteName} {hazardType}");
            things.Add(new FragmentThing(hazardThing.Id, hazardThing.Name, HazardProperties(hazard with { HazardType = hazardType })));
            Relate(siteThing, predicates.Has, hazardThing);
            BeArchetype(hazardThing, archetypes.HazardAssessment, HazardAssessmentArchetypeName);

            if (hazard.Source is not { } source)
                continue;

            var sourceName = Required(source.Name, "hazard.source.name", "a Thing is created under a name").Trim();
            var sourceKey = sourceName.ToLowerInvariant();
            if (!sourcesAlreadyMinted.TryGetValue(sourceKey, out var sourceThing))
            {
                sourceThing = new NamedThing(StableIdentity.Derive(submissionId, $"source:{sourceKey}"), sourceName);
                sourcesAlreadyMinted[sourceKey] = sourceThing;
                things.Add(new FragmentThing(sourceThing.Id, sourceThing.Name, DataSourceProperties(source)));
                Relate(siteThing, predicates.Has, sourceThing);
                BeArchetype(sourceThing, archetypes.DataSource, DataSourceArchetypeName);
            }

            Relate(hazardThing, predicates.Has, sourceThing);
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

    private static Dictionary<string, TypedValue> ProjectProperties(SubmittedProject project)
    {
        var properties = new Dictionary<string, TypedValue>();
        Write(properties, "country", VosTypeNames.String, project.Country);
        Write(properties, "nearestCity", VosTypeNames.String, project.NearestCity);
        Write(properties, "existingDataNotes", VosTypeNames.String, project.ExistingDataNotes);
        return properties;
    }

    private static Dictionary<string, TypedValue> ContactProperties(SubmittedContact contact)
    {
        var properties = new Dictionary<string, TypedValue>();
        Write(properties, "relationshipToProject", VosTypeNames.String, contact.RelationshipToProject);
        Write(properties, "emailAddress", VosTypeNames.String, contact.EmailAddress);
        Write(properties, "phoneNumber", VosTypeNames.String, contact.PhoneNumber);
        return properties;
    }

    // The share is written as given. Shares are normalised across the chosen categories further down the
    // analysis, so a set that does not reach a hundred is a wizard part-filled, and judging whether they add
    // up is a range's work on the study rather than this service's.
    private static Dictionary<string, TypedValue> AllocationProperties(SubmittedAllocation allocation)
    {
        var properties = new Dictionary<string, TypedValue>();
        Write(properties, "allocationCategory", VosTypeNames.String, allocation.Category);
        Write(properties, "sharePct", VosTypeNames.Double, allocation.SharePct);
        Write(properties, "allocatedAreaHectares", VosTypeNames.Double, allocation.AllocatedAreaHectares);
        return properties;
    }

    // The level and the date it was assessed on are absent by design. Both take observations only, written
    // when the source is resolved; a level a planner remembered would read as an assessment and is not one.
    private static Dictionary<string, TypedValue> HazardProperties(SubmittedHazard hazard)
    {
        var properties = new Dictionary<string, TypedValue>();
        Write(properties, "hazardType", VosTypeNames.String, hazard.HazardType);
        return properties;
    }

    private static Dictionary<string, TypedValue> DataSourceProperties(SubmittedDataSource source)
    {
        var properties = new Dictionary<string, TypedValue>();
        Write(properties, "coverageDescription", VosTypeNames.String, source.CoverageDescription);
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
