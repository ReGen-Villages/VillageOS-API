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

    public static ResolvedPredicates KnownPredicates => new(
        new PredicateIdentity("studies", StudiesPredicateId, Minted: false),
        new PredicateIdentity("has", HasPredicateId, Minted: false));

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
