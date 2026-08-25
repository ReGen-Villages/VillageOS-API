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
/// <c>HazardProperties</c>, <c>DataSourceProperties</c> and <c>SubmissionProperties</c>, one per Thing;
/// each property, from a
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
/// <para>
/// The two vocabularies a submission uses — what an allocation is for, and how a boundary was obtained —
/// are neither named nor listed here, and neither is written as a property. A submitted word is resolved
/// against the Things the model declares (see <see cref="DeclaredVocabularyReader"/>) and written only as
/// an edge to the one it names, so a project that adds a term edits the model and deploys nothing, and a
/// reader asking what an allocation is for follows the edge to a Thing it can ask further questions of.
/// </para>
/// </remarks>
public static class SubmissionFragmentComposer
{
    public const string SiteStudyFlag = "__IsSiteStudy";
    public const string StudiesPredicateName = "studies";
    public const string HasPredicateName = "has";
    public const string IsPredicateName = "is";
    public const string ProposesPredicateName = "proposes";

    /// <summary>What the submission record's identifier is derived under. Public because the service derives
    /// the same identifier to ask whether the record is already there.</summary>
    public const string SubmissionRole = "submission";

    public const string SiteArchetypeName = "Site";
    public const string SiteStudyArchetypeName = "SiteStudy";
    public const string ParcelArchetypeName = "Parcel";
    public const string ProjectArchetypeName = "Project";
    public const string ContactArchetypeName = "Contact";
    public const string ProgrammeAllocationArchetypeName = "ProgrammeAllocation";
    public const string HazardAssessmentArchetypeName = "HazardAssessment";
    public const string DataSourceArchetypeName = "DataSource";
    public const string SubmissionArchetypeName = "Submission";

    /// <param name="arrivedAt">When this submission reached the service, or null when the model already
    /// holds its record and its arrival is already recorded.</param>
    public static ComposedSubmission Compose(
        Submission submission, ResolvedPredicates predicates, ResolvedArchetypes archetypes,
        DeclaredVocabulary vocabulary, DateTime? arrivedAt)
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

        // The arrival itself, kept where a reviewer can ask about it. It proposes the site rather than
        // holding it: a promotion carries the group reachable from the site through `has` and `studies`,
        // and this record belongs to intake — it is resolved after the copy has landed, so a copy of it in
        // a project model would read as waiting for ever. The record asserts the edge and the site does
        // not, which is what leaves it behind when the site travels.
        var submissionThing = new NamedThing(
            StableIdentity.Derive(submissionId, SubmissionRole), $"{siteName} Submission");
        things.Add(new FragmentThing(
            submissionThing.Id, submissionThing.Name, SubmissionProperties(submissionId, arrivedAt)));
        Relate(submissionThing, predicates.Proposes, siteThing);
        BeArchetype(submissionThing, archetypes.Submission, SubmissionArchetypeName);

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

        // An edge to a term the model declares, through the predicate the model marks for that vocabulary.
        // Never minted: the term and the predicate both come from the model, and a predicate invented here
        // would carry no mark, so the readers that follow it by mark would never find the edge.
        void RelateToTerm(NamedThing subject, DeclaredTerms declared, DeclaredTerm term) =>
            Relate(subject,
                new PredicateIdentity(declared.Predicate.Name, declared.Predicate.Id, Minted: false),
                new NamedThing(term.Id, term.Name));

        Guid? parcelId = null;
        if (submission.Parcel is { } parcel)
        {
            var obtainedBy = Resolve(vocabulary.BoundarySources, "parcel.boundarySource",
                Required(parcel.BoundarySource, "parcel.boundarySource",
                    "a square generated from a stated area is not evidence and must not read as a surveyed boundary"));

            var parcelThing = new NamedThing(StableIdentity.Derive(submissionId, "parcel"), $"{siteName} Parcel-01");
            parcelId = parcelThing.Id;
            things.Add(new FragmentThing(parcelThing.Id, parcelThing.Name, ParcelProperties(parcel)));
            Relate(siteThing, predicates.Has, parcelThing);
            BeArchetype(parcelThing, archetypes.Parcel, ParcelArchetypeName);
            RelateToTerm(parcelThing, vocabulary.BoundarySources, obtainedBy);
        }

        var categoriesAlreadyGiven = new Dictionary<Guid, string>();
        foreach (var allocation in submission.Allocations ?? [])
        {
            var submitted = Required(allocation.Category, "allocation.category",
                "an allocation is a share of the land put to some named use").Trim();
            var category = Resolve(vocabulary.AllocationCategories, "allocation.category", submitted);

            // Identity comes from the category rather than from a position in the list, so a wizard that
            // reorders them re-posts onto the same Things. It comes from the resolved term rather than the
            // word submitted, so two spellings of one category are one share and not two.
            if (categoriesAlreadyGiven.TryGetValue(category.Id, out var alreadyGiven))
                throw new SubmissionError(
                    $"'allocations' gives '{alreadyGiven}' and '{submitted}' as separate shares of one "
                    + "category: they disagree about it and nothing here can say which was meant.");
            categoriesAlreadyGiven[category.Id] = submitted;

            var allocationThing = new NamedThing(
                StableIdentity.Derive(submissionId, $"allocation:{Key(category.Name)}"),
                $"{siteName} {category.Name}");
            things.Add(new FragmentThing(allocationThing.Id, allocationThing.Name,
                AllocationProperties(allocation)));
            Relate(siteThing, predicates.Has, allocationThing);
            BeArchetype(allocationThing, archetypes.ProgrammeAllocation, ProgrammeAllocationArchetypeName);
            RelateToTerm(allocationThing, vocabulary.AllocationCategories, category);
        }

        // A source named by two hazards is one Thing both hang off, not one each. Identity comes from the
        // source's name for the same reason an allocation's comes from its category.
        var sourcesAlreadyMinted = new Dictionary<string, NamedThing>();
        var coverageAlreadyGiven = new Dictionary<string, string?>();
        var hazardsAlreadyGiven = new Dictionary<string, string>();
        foreach (var hazard in submission.Hazards ?? [])
        {
            var hazardType = Required(hazard.HazardType, "hazard.hazardType",
                "an assessment is about one named hazard").Trim();
            var typeKey = Key(hazardType);
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
            var sourceKey = Key(sourceName);
            if (!sourcesAlreadyMinted.TryGetValue(sourceKey, out var sourceThing))
            {
                sourceThing = new NamedThing(StableIdentity.Derive(submissionId, $"source:{sourceKey}"), sourceName);
                sourcesAlreadyMinted[sourceKey] = sourceThing;
                coverageAlreadyGiven[sourceKey] = source.CoverageDescription;
                things.Add(new FragmentThing(sourceThing.Id, sourceThing.Name, DataSourceProperties(source)));
                Relate(siteThing, predicates.Has, sourceThing);
                BeArchetype(sourceThing, archetypes.DataSource, DataSourceArchetypeName);
            }
            else if (source.CoverageDescription is { } coverage
                     && coverageAlreadyGiven[sourceKey] is { } first && coverage != first)
            {
                // One source is one Thing, so the second mention's description has nowhere to go. Taking the
                // first silently would leave the planner believing the second was recorded.
                throw new SubmissionError(
                    $"'{sourceName}' is described two ways: '{first}' and '{coverage}'. One source is one "
                    + "Thing, so give the description once or give it the same both times.");
            }

            Relate(hazardThing, predicates.Has, sourceThing);
        }

        var fragment = new ModelFragment($"{siteName} submission", [.. mintedPredicates.Values, .. things], relationships);
        return new ComposedSubmission(fragment, submissionId, siteThing.Id, studyThing.Id, parcelId);
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

    // What the submission was called where it was filled in, so a group of Things can be traced back to it.
    // The disposition a reviewer reaches is an edge written later and never a value here: a submission
    // arrives with none, which is what reads as waiting.
    private static Dictionary<string, TypedValue> SubmissionProperties(string submissionId, DateTime? arrivedAt)
    {
        var properties = new Dictionary<string, TypedValue>();
        Write(properties, "submissionId", VosTypeNames.String, submissionId);
        Write(properties, "submittedAt", VosTypeNames.DateTime, arrivedAt);
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

    // What the allocation is for is the edge to the declared term, not a value here.
    //
    // The share is written as given. Shares are normalised across the chosen categories further down the
    // analysis, so a set that does not reach a hundred is a wizard part-filled, and judging whether they add
    // up is a range's work on the study rather than this service's.
    private static Dictionary<string, TypedValue> AllocationProperties(SubmittedAllocation allocation)
    {
        var properties = new Dictionary<string, TypedValue>();
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

    // How the boundary was obtained is the edge to the declared term, not a value here.
    private static Dictionary<string, TypedValue> ParcelProperties(SubmittedParcel parcel)
    {
        var boundary = parcel.Boundary
            ?? throw new SubmissionError("'parcel.boundary' is missing: a parcel is the boundary it encloses. "
                                       + "Leave 'parcel' out until one has been drawn.");
        if (boundary.Count < 3)
            throw new SubmissionError(
                $"'parcel.boundary' has {boundary.Count} corner(s): a boundary needs at least three.");

        return new Dictionary<string, TypedValue>
        {
            ["measuredAreaHectares"] = TypedValue.Written(VosTypeNames.Double, BoundaryGeometry.MeasureHectares(boundary)),
            ["boundary"] = TypedValue.Written(VosTypeNames.GeoJson, BoundaryGeometry.ToGeoJson(boundary)),
        };
    }

    // A word the model does not declare is refused by naming what the model does declare, so a planner is
    // corrected by the vocabulary the analysis will actually read rather than by whatever list this service
    // was compiled with. A term added to the model needs no change here.
    private static DeclaredTerm Resolve(DeclaredTerms declared, string field, string submitted)
    {
        var byKey = new Dictionary<string, DeclaredTerm>();
        foreach (var term in declared.Terms)
            if (!byKey.TryAdd(Key(term.Name), term))
                throw new ModelNotSeededError(
                    $"this model declares '{byKey[Key(term.Name)].Name}' and '{term.Name}' as separate terms, "
                    + $"and a submitted '{field}' cannot say which of them it means.");

        if (byKey.TryGetValue(Key(submitted), out var found))
            return found;

        throw new SubmissionError(declared.Terms.Count == 0
            ? $"'{field}' is '{submitted}', and this model declares no term to resolve it against."
            : $"'{field}' is '{submitted}': the model declares "
              + string.Join(", ", declared.Terms.Select(term => $"'{term.Name}'")) + ".");
    }

    private static void Write(IDictionary<string, TypedValue> properties, string name, string typeInfo, object? value)
    {
        if (value is not null)
            properties[name] = TypedValue.Written(typeInfo, value);
    }

    // What a planner named, as against how they happened to type it — which is both what a submitted word is
    // matched to a declared term by and what identifies the Thing it mints. Case and surrounding
    // space are not part of what they meant, so a wizard that re-posts with either changed lands on the Thing
    // it landed on before rather than building a second beside it.
    private static string Key(string named) => named.Trim().ToLowerInvariant();

    private static string Required(string? value, string field, string why) =>
        string.IsNullOrWhiteSpace(value) ? throw new SubmissionError($"'{field}' is missing: {why}.") : value;
}
