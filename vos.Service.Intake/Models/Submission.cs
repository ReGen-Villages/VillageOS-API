namespace vos.Service.Intake.Models;

// What an intake wizard collected, as it is posted. A submission carries its own identifier, the site's
// name, a project, and a contact with a name and an email address — the last because a submission is
// reviewed and whoever made it has to be told what was decided. Every other field is optional: a wizard
// saves as the planner fills it in, and a value that has not been given is absent rather than zero —
// the same rule that keeps a computed output declared and empty until something computes it.
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

// A hazard the planner says applies to the land, and which source says so. The level and the date
// it was assessed on are not here: those are read from the source when it is resolved, and a level a planner
// remembered is a recollection rather than an assessment.
public sealed record SubmittedHazard
{
    public string? HazardType { get; init; }

    // What the submitter says they have seen of this hazard, in the words the model declares.
    // Kept apart from the level the portal grades: the two disagreeing is the answer rather than a
    // conflict, and somebody who has watched their land flood knows what a regional model does not.
    public string? ReportedLevel { get; init; }

    // Which source says so. It becomes a Thing the assessment hangs off rather than a name copied
    // onto it, so a reader can walk from a hazard to what produced it and the two cannot disagree.
    public SubmittedDataSource? Source { get; init; }
}

// Where a figure came from. A planner asserts what it covers; when discovery resolves it, the date
// it was last resolved lands on this same Thing.
public sealed record SubmittedDataSource
{
    public string? Name { get; init; }
    public string? CoverageDescription { get; init; }
}

// How a planner would divide the land. The shares are taken as given: they are normalised across
// the categories chosen further down the analysis, so a set that does not reach a hundred is a wizard
// part-filled rather than a submission to refuse.
public sealed record SubmittedAllocation
{
    // The planner's own word for this use of the land. Deliberately not checked against a fixed
    // vocabulary: which categories roll into which footprint is configuration on the analysis node, so a
    // project with its own programme vocabulary must not need a change here.
    public string? Category { get; init; }

    public double? SharePct { get; init; }
}

// What the submission is for. Its properties are the planner's own account of the undertaking,
// which is why every one of them is a Fact rather than something a provider refreshes.
public sealed record SubmittedProject
{
    public string? Name { get; init; }
    public string? Country { get; init; }
    public string? NearestCity { get; init; }

    // What the planner already holds about the land. It is prose because the point is to capture
    // what a discovery pass should not go looking for, which no fixed vocabulary would cover.
    public string? ExistingDataNotes { get; init; }
}

// Who to ask about the submission. Kept as its own Thing rather than properties on the project so
// that who to ask can change without rewriting what is being asked about.
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

    // What the planner asserted the land measures. Kept apart from the parcel's measured area:
    // comparing the two is the check the intake flow exists to make.
    public double? StatedAreaHectares { get; init; }

    public long? Population { get; init; }
    public double? HouseholdSize { get; init; }
}

public sealed record SubmittedParcel
{
    // How the boundary was arrived at. A square generated from a stated area is not evidence of
    // anything and must not read as a surveyed one.
    public string? BoundarySource { get; init; }

    public IReadOnlyList<BoundaryPoint>? Boundary { get; init; }
}

// One corner of a boundary. The fields are named rather than positional because a coordinate pair
// read in the wrong order is a mistake nothing downstream can catch.
public sealed record BoundaryPoint
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
