using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Tests;

/// <summary>The worked example from <c>docs/LAND_INTAKE.md</c> §7 — a fictional 24-hectare site in Portugal
/// for 320 residents. The platform's own fixture applies the same figures, so the two can be read against
/// each other.</summary>
public static class WillowBend
{
    /// <summary>What a wizard generates: an identifier nobody else could arrive at, which is what keeps one
    /// submission from landing on the Things another submission minted.</summary>
    public const string SubmissionId = "9f1c74d6-0b8e-4a52-bd31-6c7e5a92f048";

    public static readonly Guid StudiesPredicateId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid HasPredicateId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid IsPredicateId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ProposesPredicateId = new("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    public static ResolvedPredicates KnownPredicates => new(
        new PredicateIdentity("studies", StudiesPredicateId, Minted: false),
        new PredicateIdentity("has", HasPredicateId, Minted: false),
        new PredicateIdentity("is", IsPredicateId, Minted: false),
        new PredicateIdentity("proposes", ProposesPredicateId, Minted: false));

    /// <summary>A moment to stamp an arrival with. Fixed, because a test that read the real clock could
    /// only assert that the value was close to it.</summary>
    public static readonly DateTime ArrivedAt = new(2026, 8, 22, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>The archetypes a model seeded from the shared analysis template and the land-intake
    /// template holds. A submission points its Things at these rather than declaring their properties
    /// again.</summary>
    public static readonly Guid SiteArchetypeId = new("44444444-4444-4444-4444-444444444444");
    public static readonly Guid SiteStudyArchetypeId = new("55555555-5555-5555-5555-555555555555");
    public static readonly Guid ParcelArchetypeId = new("66666666-6666-6666-6666-666666666666");
    public static readonly Guid ProjectArchetypeId = new("77777777-7777-7777-7777-777777777777");
    public static readonly Guid ContactArchetypeId = new("88888888-8888-8888-8888-888888888888");
    public static readonly Guid ProgrammeAllocationArchetypeId = new("99999999-9999-9999-9999-999999999999");
    public static readonly Guid HazardAssessmentArchetypeId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid DataSourceArchetypeId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid SubmissionArchetypeId = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    public static ResolvedArchetypes KnownArchetypes =>
        new(SiteArchetypeId, SiteStudyArchetypeId, ParcelArchetypeId, ProjectArchetypeId, ContactArchetypeId,
            ProgrammeAllocationArchetypeId, HazardAssessmentArchetypeId, DataSourceArchetypeId,
            SubmissionArchetypeId);

    public static readonly Guid CategorizedAsPredicateId = new("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public static readonly Guid ObtainedByPredicateId = new("dddddddd-dddd-dddd-dddd-dddddddddddd");
    public static readonly Guid AssessesPredicateId = new("0a0a0a0a-0a0a-0a0a-0a0a-0a0a0a0a0a0a");

    /// <summary>The vocabularies the land-intake template declares, under the names it declares them
    /// with. A submission names a term as the model spells it; how the term is displayed is the wizard's
    /// business and never reaches here.</summary>
    public static readonly string[] AllocationCategoryNames =
    [
        "residential", "food-and-agriculture", "green-water-and-restoration", "commercial-and-retail",
        "community-education-and-health", "mobility-and-infrastructure",
    ];

    public static readonly string[] BoundarySourceNames =
        ["drawn-by-hand", "imported-from-file", "generated-from-stated-area"];

    public static readonly string[] HazardTypeNames =
    [
        "river-flood", "landslide", "wildfire", "earthquake", "cyclone", "extreme-heat", "water-scarcity",
        "urban-flood",
    ];

    /// <summary>What a deployment's model says a map may draw on. Declared under its own archetype rather
    /// than by a mark, which is the contract <c>docs/TRELLIS.md</c> §22 states for every client that draws
    /// one — the address and the credit are the model's, and no provider is named in code anywhere.
    /// </summary>
    public const string VectorBasemapName = "Streets";
    public const string VectorBasemapStyleUrl = "https://basemaps.example.test/styles/streets.json";
    public const string BasemapAttribution = "© the basemap provider";

    public static readonly Guid IsInPredicateId = StableIdentity.Derive("isIn", "predicate");
    public static readonly Guid RootPlaceId = StableIdentity.Derive("Earth", "place");

    public static DeclaredVocabulary KnownVocabulary => new(
        Declared(CategorizedAsPredicateId, "categorizedAs", AllocationCategoryNames),
        Declared(ObtainedByPredicateId, "obtainedBy", BoundarySourceNames),
        Declared(AssessesPredicateId, "assesses", HazardTypeNames),
        new DeclaredPlace(new DeclaredTerm("isIn", IsInPredicateId), new DeclaredTerm("Earth", RootPlaceId)));

    public static Guid TermId(string name) => StableIdentity.Derive(name, "term");

    private static DeclaredTerms Declared(Guid predicateId, string predicateName, IEnumerable<string> terms) =>
        new(new DeclaredTerm(predicateName, predicateId),
            [.. terms.Select(name => new DeclaredTerm(name, TermId(name)))]);

    public static Submission Submission() => new()
    {
        SubmissionId = SubmissionId,
        Project = new SubmittedProject
        {
            Name = "Willow Bend Regeneration",
            Country = "Portugal",
            NearestCity = "Santarém",
            ExistingDataNotes = "Rainfall figures held from a 2024 survey; no solar measurements.",
        },
        Contact = new SubmittedContact
        {
            Name = "Ana Ferreira",
            RelationshipToProject = "landowner",
            EmailAddress = "ana.ferreira@example.pt",
            PhoneNumber = "+351 200 000 000",
        },
        Site = new SubmittedSite
        {
            Name = "Willow Bend",
            Latitude = 39.5012,
            Longitude = -8.4137,
            StatedAreaHectares = 24.0,
            Population = 320,
            HouseholdSize = 2.4,
        },
        // The programme from LAND_INTAKE.md §8, each category named as the model declares it. The shares add
        // to 100 there, but nothing in the producer requires it — they are normalised across the chosen
        // categories further down the analysis.
        Allocations =
        [
            new SubmittedAllocation { Category = "residential", SharePct = 22 },
            new SubmittedAllocation { Category = "food-and-agriculture", SharePct = 34 },
            new SubmittedAllocation { Category = "green-water-and-restoration", SharePct = 20 },
            new SubmittedAllocation { Category = "commercial-and-retail", SharePct = 8 },
            new SubmittedAllocation { Category = "community-education-and-health", SharePct = 9 },
            new SubmittedAllocation { Category = "mobility-and-infrastructure", SharePct = 7 },
        ],
        // Two hazards read off one portal, so the source is one Thing both hang off. LAND_INTAKE.md §8
        // calls a level without a source a recollection rather than an assessment, so neither is here.
        Hazards =
        [
            new SubmittedHazard
            {
                HazardType = "river-flood",
                Source = new SubmittedDataSource
                {
                    Name = "National flood portal",
                    CoverageDescription = "Mainland river catchments, updated yearly.",
                },
            },
            new SubmittedHazard
            {
                HazardType = "wildfire",
                Source = new SubmittedDataSource { Name = "National flood portal" },
            },
        ],
        Parcel = new SubmittedParcel
        {
            BoundarySource = "drawn-by-hand",
            // A boundary that encloses 23.4 hectares against the 24.0 the planner stated — the disagreement
            // the document's worked example turns on.
            Boundary =
            [
                new BoundaryPoint { Latitude = 39.4990248, Longitude = -8.4165190 },
                new BoundaryPoint { Latitude = 39.4990248, Longitude = -8.4108810 },
                new BoundaryPoint { Latitude = 39.5033752, Longitude = -8.4108810 },
                new BoundaryPoint { Latitude = 39.5033752, Longitude = -8.4165190 },
            ],
        },
    };
}
