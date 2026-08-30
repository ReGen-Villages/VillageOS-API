using System.Text.Json;
using vos.Service.Intake.Models;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

/// <summary>
/// What the page a submitter opens is drawn from, read out of the model that page cannot read for itself.
/// </summary>
/// <remarks>
/// Nothing here names a dashboard, an archetype or a predicate. The page is whichever Thing the model
/// marks as the one a submitter may read; the Things that never travel are whichever archetype it marks
/// as carrying personal details; and the edges the reading follows are the ones that page's own spec
/// walks. So a project may re-author the page, and what a submitter is sent follows it without a change
/// here.
/// <para>
/// The walk is rooted at one site and follows each edge the way the spec follows it. A selector naming
/// the archetypes instead would answer with every Thing of that kind in the model — which in a staging
/// model is every other submitter's land.
/// </para>
/// <para>
/// A snapshot is read here as the broker wrote it rather than through <see cref="SnapshotDocument"/>,
/// because what the answer carries is the broker's own envelope and not a shape of this service's. The
/// broker serializes with no naming policy, so the payload's own fields are lower-case and everything it
/// serialized off a Thing keeps the capital it was declared with.
/// </para>
/// </remarks>
public static class FindingsReader
{
    /// <summary>The mark on the page a submitter may read. Declared in the platform repository's
    /// <c>land-intake.template.json</c>; the two agree by this literal and by nothing else.</summary>
    public const string FindingsDashboardFlag = "__IsSubmitterFindingsDashboard";

    /// <summary>The mark on the archetype whose members never leave the model.</summary>
    public const string PersonalDetailFlag = "__IsPersonalDetailArchetype";

    public const string SpecProperty = "spec";
    public const string EmailAddressProperty = "emailAddress";

    /// <summary>What the model declares, plus the contact of the one submission being asked about. Read
    /// together because none of it is answered to anybody: the address is compared against the ticket and
    /// dropped, and the marks decide what the second read may carry.</summary>
    public static SubscriptionSelector DeclarationSelector(Guid contactId) => new()
    {
        Ids = [contactId],
        MarkedArchetypes = [FindingsDashboardFlag, PersonalDetailFlag],
        IncludeIsAncestors = false,
        IncludeRelationships = false,
    };

    public static SubmitterDeclarations ReadDeclarations(JsonElement snapshot, Guid contactId)
    {
        var things = Things(snapshot).ToList();

        var page = things.Where(thing => Marked(thing, FindingsDashboardFlag)).ToArray();
        var personal = things.Where(thing => Marked(thing, PersonalDetailFlag)).ToArray();
        RefuseAModelThatCannotAnswerSafely(page, personal);

        var spec = PropertyText(page[0], SpecProperty)
            ?? throw new ModelNotSeededError(
                $"the page marked '{FindingsDashboardFlag}' carries no '{SpecProperty}', so there is "
                + "nothing to draw. Seed the model from the analysis templates.");

        var contact = things.FirstOrDefault(thing => Identifier(thing) == contactId);
        return new SubmitterDeclarations(
            spec,
            Identifier(personal[0]),
            contact is { } named ? PropertyText(named, EmailAddressProperty) : null);
    }

    /// <summary>The reading the page is drawn from: the site, and what the spec's own walks reach from
    /// it. Depth is unbounded because a walk that nests — a site to its parcel to where the boundary came
    /// from — is one the spec writes as one step; the platform stops as soon as a hop reaches nothing new.
    /// </summary>
    public static SubscriptionSelector FindingsSelector(Guid siteId, string spec)
    {
        var walked = WalksIn(spec);
        return new SubscriptionSelector
        {
            Ids = [siteId],
            // A predicate is a Thing, and what draws the reading reads an edge's predicate by looking that
            // Thing up. Asked for alongside the edges they join, or an `is` edge is one nothing can read
            // and no Thing can be told what it is.
            Names =
            [
                .. walked.Select(walk => walk.Predicate)
                    .Append(SubmissionFragmentComposer.IsPredicateName)
                    .Distinct(),
            ],
            Traverse =
            [
                .. walked.Select(walk => new TraverseRule
                {
                    Predicate = walk.Predicate,
                    Direction = walk.Inbound ? "incoming" : "outgoing",
                    Depth = int.MaxValue,
                }),
            ],
        };
    }

    /// <summary>Everything the reading reached except what carries personal details, which is dropped
    /// whatever walk arrived at it rather than by trusting that no walk does.</summary>
    public static List<JsonElement> ThingsToAnswerWith(JsonElement snapshot, Guid personalDetailArchetype)
    {
        var things = Things(snapshot).ToList();
        var withheld = ArchetypeAndItsMembers(personalDetailArchetype, things, snapshot);
        return [.. things.Where(thing => !withheld.Contains(Identifier(thing)))];
    }

    /// <summary>The edges between the Things being answered with. An edge to one that was withheld is
    /// withheld with it: it would name an identifier the answer does not carry, and say that the site
    /// relates to something the reader is not shown.</summary>
    public static List<JsonElement> RelationshipsToAnswerWith(JsonElement snapshot, IEnumerable<JsonElement> things)
    {
        var answered = things.Select(Identifier).ToHashSet();
        return
        [
            .. Relationships(snapshot).Where(edge =>
                answered.Contains(Field(edge, "SubjectId")) && answered.Contains(Field(edge, "TargetId"))),
        ];
    }

    /// <summary>Whose ranges the answer has to carry: every Thing holding a state. A verdict row is drawn
    /// only for a Thing in one of the states its spec names, and the target it reads comes off the range
    /// that judged it — so this is exactly the set the page will ask about, and no more.</summary>
    public static List<Guid> JudgedThings(IEnumerable<JsonElement> things) =>
    [
        .. things
            .Where(thing => MyceliumClientBase.TryGetPropertyCaseInsensitive(thing, "States", out var states)
                            && states.ValueKind == JsonValueKind.Array
                            && states.GetArrayLength() > 0)
            .Select(Identifier),
    ];

    // Both marks or neither. A model holding the page but not the mark on what carries personal details
    // would answer a submitter with a reading nothing was filtered out of, and that filter is the only
    // thing standing between a walk and a landowner's address. Two Things carrying one mark is the same
    // refusal: which of them was meant is not this service's to guess.
    private static void RefuseAModelThatCannotAnswerSafely(JsonElement[] page, JsonElement[] personal)
    {
        var wrong = new[]
        {
            page.Length == 1 ? null : $"{page.Length} Things marked '{FindingsDashboardFlag}'",
            personal.Length == 1 ? null : $"{personal.Length} Things marked '{PersonalDetailFlag}'",
        }.OfType<string>().ToArray();

        if (wrong.Length > 0)
            throw new ModelNotSeededError(
                $"this model holds {string.Join(" and ", wrong)}, and answering a submitter needs exactly "
                + "one of each: a page to draw, and the archetype whose members never travel with it. "
                + "Seed the model from the analysis templates.");
    }

    /// <summary>One edge a spec follows. Both shapes a spec writes a walk in — a step in a binding's
    /// <c>via</c>, and the predicate a scope narrows through — are the same question of the model.</summary>
    private readonly record struct Walk(string Predicate, bool Inbound);

    private static List<Walk> WalksIn(string spec)
    {
        using var parsed = JsonDocument.Parse(spec);
        var found = new HashSet<Walk>();
        Collect(parsed.RootElement, found);
        // In a settled order, so one page always asks the platform the same question — a set's own order
        // is not one, and a selector that varied between reads would be one nobody could reproduce.
        return [.. found.OrderBy(walk => walk.Predicate, StringComparer.Ordinal).ThenBy(walk => walk.Inbound)];
    }

    private static readonly string[] PredicateFields = ["predicate", "viaPredicate"];

    private static void Collect(JsonElement element, HashSet<Walk> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var field in PredicateFields)
                {
                    if (element.TryGetProperty(field, out var predicate)
                        && predicate.ValueKind == JsonValueKind.String
                        && predicate.GetString() is { Length: > 0 } named)
                    {
                        found.Add(new Walk(named, Inbound: FieldText(element, "direction") == "in"));
                    }
                }

                foreach (var property in element.EnumerateObject()) Collect(property.Value, found);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, found);
                break;
        }
    }

    private static HashSet<Guid> ArchetypeAndItsMembers(Guid archetype, List<JsonElement> things, JsonElement snapshot)
    {
        var isPredicate = things
            .Where(thing => FieldText(thing, "Name") == SubmissionFragmentComposer.IsPredicateName)
            .Select(Identifier)
            .ToHashSet();

        var withheld = Relationships(snapshot)
            .Where(edge => isPredicate.Contains(Field(edge, "PredicateId")) && Field(edge, "TargetId") == archetype)
            .Select(edge => Field(edge, "SubjectId"))
            .ToHashSet();
        withheld.Add(archetype);
        return withheld;
    }

    private static bool Marked(JsonElement thing, string flag) =>
        Property(thing, flag) is { } carried
        && MyceliumClientBase.TryGetPropertyCaseInsensitive(carried, "value", out var value)
        && value.ValueKind == JsonValueKind.True;

    private static IEnumerable<JsonElement> Things(JsonElement snapshot) => Section(snapshot, "things");

    private static IEnumerable<JsonElement> Relationships(JsonElement snapshot) => Section(snapshot, "relationships");

    private static IEnumerable<JsonElement> Section(JsonElement snapshot, string name) =>
        MyceliumClientBase.TryGetPropertyCaseInsensitive(snapshot, name, out var section)
        && section.ValueKind == JsonValueKind.Array
            ? section.EnumerateArray()
            : [];

    public static Guid Identifier(JsonElement entity) => Field(entity, "Id");

    private static Guid Field(JsonElement entity, string name) =>
        MyceliumClientBase.TryGetPropertyCaseInsensitive(entity, name, out var value)
        && value.TryGetGuid(out var identifier)
            ? identifier
            : Guid.Empty;

    private static string? FieldText(JsonElement entity, string name) =>
        MyceliumClientBase.TryGetPropertyCaseInsensitive(entity, name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement? Property(JsonElement thing, string name) =>
        MyceliumClientBase.TryGetPropertyCaseInsensitive(thing, "Properties", out var properties)
        && MyceliumClientBase.TryGetPropertyCaseInsensitive(properties, name, out var property)
            ? property
            : null;

    /// <summary>A property's value as text. Everything read this way the model wrote as a string.</summary>
    private static string? PropertyText(JsonElement thing, string name) =>
        Property(thing, name) is { } property
        && MyceliumClientBase.TryGetPropertyCaseInsensitive(property, "value", out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
