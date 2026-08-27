using vos.Service.Intake.Models;

namespace vos.Service.Intake;

/// <summary>
/// What a submission may contain, and the check that it does. Anyone may post to this service, so every
/// bound here is a bound on what a stranger can make the model hold.
/// </summary>
/// <remarks>
/// A refusal names the field and never quotes the value back. Contact details arrive on this route by
/// design, and a message that echoed one would put it in whatever reads the response — see
/// <c>docs/LAND_INTAKE.md</c> §12.
/// </remarks>
public static class SubmissionLimits
{
    /// <summary>A submission is a form's worth of answers and a drawn boundary — kilobytes. The cap is
    /// generous against that and small enough that a body cannot cost the service its memory before
    /// anything has looked at it.</summary>
    public const long MaximumBodyBytes = 256 * 1024;

    /// <summary>A verification body is an address and a six-figure code. Nothing that size can cost this
    /// service anything, and a body far past it was never one.</summary>
    public const long MaximumVerificationBytes = 1024;

    /// <summary>A name, a country, a relationship, an address, a phone number.</summary>
    public const int LongestText = 200;

    /// <summary>A field a person writes sentences into.</summary>
    public const int LongestProse = 4_000;

    public const int MostAllocations = 50;
    public const int MostHazards = 50;

    /// <summary>Generous against a hand-drawn parcel and against a boundary imported from a survey file,
    /// which is where a corner count of any size would come from.</summary>
    public const int MostBoundaryCorners = 2_000;

    /// <summary>Larger than any single landholding and smaller than a country, so a figure entered in
    /// square metres is caught rather than composed.</summary>
    public const double LargestAreaHectares = 1_000_000;

    public const long LargestPopulation = 10_000_000;
    public const double LargestHouseholdSize = 100;

    public static void Enforce(Submission submission)
    {
        Identifier(submission.SubmissionId);

        if (submission.Site is { } site)
        {
            Text(site.Name, "site.name");
            Between(site.Latitude, -90, 90, "site.latitude", "degrees of latitude");
            Between(site.Longitude, -180, 180, "site.longitude", "degrees of longitude");
            Between(site.StatedAreaHectares, 0, LargestAreaHectares, "site.statedAreaHectares", "hectares");
            Between(site.Population, 0, LargestPopulation, "site.population", "residents");
            Between(site.HouseholdSize, 1, LargestHouseholdSize, "site.householdSize", "people to a household");
        }

        if (submission.Project is { } project)
        {
            Text(project.Name, "project.name");
            Text(project.Country, "project.country");
            Text(project.NearestCity, "project.nearestCity");
            Prose(project.ExistingDataNotes, "project.existingDataNotes");
        }

        if (submission.Contact is { } contact)
        {
            Text(contact.Name, "contact.name");
            Text(contact.RelationshipToProject, "contact.relationshipToProject");
            Text(contact.EmailAddress, "contact.emailAddress");
            EmailAddress(contact.EmailAddress, "contact.emailAddress");
            Text(contact.PhoneNumber, "contact.phoneNumber");
        }

        if (submission.Parcel is { } parcel)
        {
            Text(parcel.BoundarySource, "parcel.boundarySource");
            Boundary(parcel.Boundary);
        }

        AtMost(submission.Allocations?.Count, MostAllocations, "allocations", "shares of the land");
        foreach (var allocation in submission.Allocations ?? [])
        {
            Text(allocation.Category, "allocation.category");
            Between(allocation.SharePct, 0, 100, "allocation.sharePct", "per cent");
        }

        AtMost(submission.Hazards?.Count, MostHazards, "hazards", "assessments");
        foreach (var hazard in submission.Hazards ?? [])
        {
            Text(hazard.HazardType, "hazard.hazardType");
            if (hazard.Source is not { } source) continue;
            Text(source.Name, "hazard.source.name");
            Prose(source.CoverageDescription, "hazard.source.coverageDescription");
        }
    }

    // Every Thing a submission mints derives its identity from this, so two submissions carrying one
    // identifier are one site. Where anybody may post, an identifier anybody could arrive at is a way to
    // write over a submission somebody else made.
    private static void Identifier(string? submissionId)
    {
        if (submissionId is null || Guid.TryParse(submissionId, out _)) return;

        throw new SubmissionError(
            "'submissionId' is not a unique identifier: every Thing a submission mints derives its identity "
            + "from it, so it has to be one nobody else could arrive at.");
    }

    private static void Boundary(IReadOnlyList<BoundaryPoint>? boundary)
    {
        if (boundary is null) return;

        AtMost(boundary.Count, MostBoundaryCorners, "parcel.boundary", "corners");
        foreach (var corner in boundary)
        {
            Between(corner.Latitude, -90, 90, "parcel.boundary", "degrees of latitude");
            Between(corner.Longitude, -180, 180, "parcel.boundary", "degrees of longitude");
        }
    }

    // Whoever reviews a submission has to be able to tell the submitter what was decided, and this is the
    // only way back to them. The shape is as far as a form gets on its own: it catches a mistake in the
    // typing, and that somebody reads what is sent there is what verification establishes.
    public static void EmailAddress(string? value, string field)
    {
        if (value is null || CouldBeWrittenTo(value.Trim())) return;

        throw new SubmissionError(
            $"'{field}' is not the shape of an email address: a mailbox, an '@', "
            + "and a host with a dot in it.");
    }

    private static bool CouldBeWrittenTo(string address)
    {
        if (address.Any(char.IsWhiteSpace)) return false;

        var mailboxAndHost = address.Split('@');
        if (mailboxAndHost.Length != 2 || mailboxAndHost[0].Length == 0) return false;

        var labels = mailboxAndHost[1].Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length > 0) && labels[^1].Length >= 2;
    }

    private static void Text(string? value, string field) => NoLongerThan(value, LongestText, field);

    private static void Prose(string? value, string field) => NoLongerThan(value, LongestProse, field);

    private static void NoLongerThan(string? value, int longest, string field)
    {
        if (value is null || value.Length <= longest) return;

        throw new SubmissionError($"'{field}' is longer than the {longest} characters it holds.");
    }

    private static void AtMost(int? given, int most, string field, string ofWhat)
    {
        if (given is null || given <= most) return;

        throw new SubmissionError($"'{field}' holds at most {most} {ofWhat}.");
    }

    private static void Between(double? value, double least, double most, string field, string ofWhat)
    {
        if (value is null || (value >= least && value <= most)) return;

        throw new SubmissionError($"'{field}' is outside the range it takes, {least} to {most} {ofWhat}.");
    }
}
