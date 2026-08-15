using vos.Service.Intake.Models;

namespace vos.Service.Intake.Tests;

/// <summary>The worked example from <c>docs/LAND_INTAKE.md</c> §7 — a fictional 24-hectare site in Portugal
/// for 320 residents. The platform's own fixture applies the same figures, so the two can be read against
/// each other.</summary>
public static class WillowBend
{
    public const string SubmissionId = "willow-bend-2026-08";

    public static readonly Guid StudiesPredicateId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid HasPredicateId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid IsPredicateId = new("33333333-3333-3333-3333-333333333333");

    public static ResolvedPredicates KnownPredicates => new(
        new PredicateIdentity("studies", StudiesPredicateId, Minted: false),
        new PredicateIdentity("has", HasPredicateId, Minted: false),
        new PredicateIdentity("is", IsPredicateId, Minted: false));

    /// <summary>The archetypes a model seeded from the shared analysis template and the land-intake
    /// template holds. A submission points its Things at these rather than declaring their properties
    /// again.</summary>
    public static readonly Guid SiteArchetypeId = new("44444444-4444-4444-4444-444444444444");
    public static readonly Guid SiteStudyArchetypeId = new("55555555-5555-5555-5555-555555555555");
    public static readonly Guid ParcelArchetypeId = new("66666666-6666-6666-6666-666666666666");

    public static ResolvedArchetypes KnownArchetypes =>
        new(SiteArchetypeId, SiteStudyArchetypeId, ParcelArchetypeId);

    public static Submission Submission() => new()
    {
        SubmissionId = SubmissionId,
        Site = new SubmittedSite
        {
            Name = "Willow Bend",
            Latitude = 39.5012,
            Longitude = -8.4137,
            StatedAreaHectares = 24.0,
            Population = 320,
            HouseholdSize = 2.4,
        },
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
