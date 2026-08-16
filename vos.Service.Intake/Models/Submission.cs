namespace vos.Service.Intake.Models;

/// <summary>
/// What an intake wizard collected, as it is posted. Every field beyond the submission's own identifier
/// and the site's name is optional: a wizard saves as the planner fills it in, and a value that has not
/// been given is absent rather than zero — the same rule that keeps a computed output declared and empty
/// until something computes it.
/// </summary>
public sealed record Submission
{
    public string? SubmissionId { get; init; }
    public SubmittedProject? Project { get; init; }
    public SubmittedContact? Contact { get; init; }
    public SubmittedSite? Site { get; init; }
    public SubmittedParcel? Parcel { get; init; }
    public IReadOnlyList<SubmittedAllocation>? Allocations { get; init; }
    public IReadOnlyList<SubmittedHazard>? Hazards { get; init; }
}

/// <summary>A hazard the planner says applies to the land, and which source says so. The level and the date
/// it was assessed on are not here: those are read from the source when it is resolved, and a level a planner
/// remembered is a recollection rather than an assessment.</summary>
public sealed record SubmittedHazard
{
    public string? HazardType { get; init; }

    /// <summary>Which source says so. It becomes a Thing the assessment hangs off rather than a name copied
    /// onto it, so a reader can walk from a hazard to what produced it and the two cannot disagree.</summary>
    public SubmittedDataSource? Source { get; init; }
}

/// <summary>Where a figure came from. A planner asserts what it covers; when discovery resolves it, the date
/// it was last resolved lands on this same Thing.</summary>
public sealed record SubmittedDataSource
{
    public string? Name { get; init; }
    public string? CoverageDescription { get; init; }
}

/// <summary>How a planner would divide the land. The shares are taken as given: they are normalised across
/// the categories chosen further down the analysis, so a set that does not reach a hundred is a wizard
/// part-filled rather than a submission to refuse.</summary>
public sealed record SubmittedAllocation
{
    /// <summary>The planner's own word for this use of the land. Deliberately not checked against a fixed
    /// vocabulary: which categories roll into which footprint is configuration on the analysis node, so a
    /// project with its own programme vocabulary must not need a change here.</summary>
    public string? Category { get; init; }

    public double? SharePct { get; init; }
    public double? AllocatedAreaHectares { get; init; }
}

/// <summary>What the submission is for. Its properties are the planner's own account of the undertaking,
/// which is why every one of them is a Fact rather than something a provider refreshes.</summary>
public sealed record SubmittedProject
{
    public string? Name { get; init; }
    public string? Country { get; init; }
    public string? NearestCity { get; init; }

    /// <summary>What the planner already holds about the land. It is prose because the point is to capture
    /// what a discovery pass should not go looking for, which no fixed vocabulary would cover.</summary>
    public string? ExistingDataNotes { get; init; }
}

/// <summary>Who to ask about the submission. Kept as its own Thing rather than properties on the project so
/// that who to ask can change without rewriting what is being asked about.</summary>
public sealed record SubmittedContact
{
    public string? Name { get; init; }
    public string? RelationshipToProject { get; init; }
    public string? EmailAddress { get; init; }
    public string? PhoneNumber { get; init; }
}

public sealed record SubmittedSite
{
    public string? Name { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }

    /// <summary>What the planner asserted the land measures. Kept apart from the parcel's measured area:
    /// comparing the two is the check the intake flow exists to make.</summary>
    public double? StatedAreaHectares { get; init; }

    public long? Population { get; init; }
    public double? HouseholdSize { get; init; }
}

public sealed record SubmittedParcel
{
    /// <summary>How the boundary was arrived at. A square generated from a stated area is not evidence of
    /// anything and must not read as a surveyed one.</summary>
    public string? BoundarySource { get; init; }

    public IReadOnlyList<BoundaryPoint>? Boundary { get; init; }
}

/// <summary>One corner of a boundary. The fields are named rather than positional because a coordinate pair
/// read in the wrong order is a mistake nothing downstream can catch.</summary>
public sealed record BoundaryPoint
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
