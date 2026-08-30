using System.Text.Json;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Services;

namespace vos.Service.Intake.Tests;

/// <summary>
/// A snapshot as the broker writes one, rather than as a typed record round-trips one.
/// </summary>
/// <remarks>
/// The findings a submitter is answered with are the broker's own envelope passed through, so what a test
/// feeds in has to be that envelope: the payload's own fields lower-case, a Thing's fields keeping the
/// capital they were declared with, and a property carrying <c>typeInfo</c>, <c>value</c> and the
/// <c>writeKind</c> that says whether a figure was stated or measured. A fixture built out of
/// <see cref="vos.Service.Shared.Subscriptions.SnapshotDocument"/> carries neither the capitals nor the
/// write kind, so a test written on one would pass over a service that answers a page it cannot draw.
/// </remarks>
public sealed class BrokerSnapshot
{
    private static readonly JsonSerializerOptions AsTheBrokerWritesIt = new() { PropertyNamingPolicy = null };

    private readonly List<Node> _things = [];
    private readonly List<Edge> _edges = [];

    private sealed record Node(Guid Id, string Name, bool IsArchetype, Dictionary<string, object> Properties, string[] States);

    private sealed record Edge(Guid Id, Guid SubjectId, Guid PredicateId, Guid TargetId);

    public BrokerSnapshot Thing(
        Guid id,
        string name,
        bool isArchetype = false,
        Dictionary<string, object>? properties = null,
        string[]? states = null)
    {
        _things.Add(new Node(id, name, isArchetype, properties ?? [], states ?? []));
        return this;
    }

    public BrokerSnapshot Relate(Guid subject, Guid predicate, Guid target)
    {
        _edges.Add(new Edge(Guid.NewGuid(), subject, predicate, target));
        return this;
    }

    /// <summary>The model as a deployment that was never seeded for this holds it: the page and the
    /// archetype are there, and nothing says which is which.</summary>
    public BrokerSnapshot WithoutTheMarks()
    {
        foreach (var flag in (string[])[FindingsReader.FindingsDashboardFlag, FindingsReader.PersonalDetailFlag])
            foreach (var thing in _things)
                thing.Properties.Remove(flag);
        return this;
    }

    /// <summary>A Thing the model no longer holds — a submission cleared once its retention period ran.
    /// </summary>
    public BrokerSnapshot Without(Guid id)
    {
        _things.RemoveAll(thing => thing.Id == id);
        _edges.RemoveAll(edge => edge.SubjectId == id || edge.TargetId == id);
        return this;
    }

    /// <summary>A value the model holds, in the envelope the broker writes one in.</summary>
    public static Dictionary<string, object> Stating(params (string Name, object Value, string? WriteKind)[] values) =>
        values.ToDictionary(
            stated => stated.Name,
            stated => stated.WriteKind is { } kind
                ? (object)new { typeInfo = "vos.Double", value = stated.Value, writeKind = kind }
                : new { typeInfo = "vos.Double", value = stated.Value });

    /// <summary>What <c>POST /api/subscriptions</c> answers a selector that takes the model whole.</summary>
    public string Opened() => Written(_things, _edges);

    /// <summary>What it answers a selector rooted at one site: that site, what the page's own walks reach
    /// from it, and what each of those <c>is</c>. This is the platform's job, reproduced here because the
    /// whole guarantee a submitter is given rests on the walk starting at one Thing.</summary>
    public string OpenedReaching(Guid site)
    {
        var reached = new HashSet<Guid> { site };
        bool grew;
        do
        {
            grew = false;
            foreach (var edge in _edges)
            {
                var outward = edge.PredicateId == HasPredicate || edge.PredicateId == ObtainedByPredicate;
                var inward = edge.PredicateId == StudiesPredicate;
                if (outward && reached.Contains(edge.SubjectId)) grew |= reached.Add(edge.TargetId);
                if (inward && reached.Contains(edge.TargetId)) grew |= reached.Add(edge.SubjectId);
            }
        }
        while (grew);

        // What each reached Thing `is`, and the predicates the selector named, which the platform includes
        // because a page reads an edge's predicate by looking that Thing up.
        foreach (var edge in _edges.Where(edge => edge.PredicateId == IsPredicate && reached.Contains(edge.SubjectId)).ToArray())
            reached.Add(edge.TargetId);
        foreach (var predicate in (Guid[])[IsPredicate, HasPredicate, StudiesPredicate, ObtainedByPredicate])
            reached.Add(predicate);

        var things = _things.Where(thing => reached.Contains(thing.Id)).ToList();
        return Written(
            things,
            _edges.Where(edge => reached.Contains(edge.SubjectId) && reached.Contains(edge.TargetId)).ToList());
    }

    public JsonDocument Parsed() =>
        JsonDocument.Parse(JsonSerializer.Serialize(Snapshot(_things, _edges), AsTheBrokerWritesIt));

    private static string Written(List<Node> things, List<Edge> edges) => JsonSerializer.Serialize(
        new { subscriptionId = Guid.NewGuid(), watermark = 0, snapshot = Snapshot(things, edges) },
        AsTheBrokerWritesIt);

    private static object Snapshot(List<Node> things, List<Edge> edges) => new
    {
        watermark = 0,
        things = things.Select(thing => new
        {
            thing.Id,
            thing.Name,
            thing.IsArchetype,
            thing.Properties,
            InheritedOverrides = (object?)null,
            thing.States,
        }),
        relationships = edges.Select(edge => new
        {
            edge.Id,
            Name = (string?)null,
            edge.SubjectId,
            edge.PredicateId,
            edge.TargetId,
            Properties = new Dictionary<string, object>(),
            States = Array.Empty<string>(),
        }),
    };

    // ---- the worked example -------------------------------------------------

    public const string OtherSubmissionId = "1d2c3b4a-5e6f-4708-9a1b-2c3d4e5f6071";

    public static readonly Guid IsPredicate = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid HasPredicate = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid StudiesPredicate = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ObtainedByPredicate = new("dddddddd-dddd-dddd-dddd-dddddddddddd");

    public static readonly Guid SiteArchetype = new("44444444-4444-4444-4444-444444444444");
    public static readonly Guid StudyArchetype = new("55555555-5555-5555-5555-555555555555");
    public static readonly Guid ParcelArchetype = new("66666666-6666-6666-6666-666666666666");
    public static readonly Guid ContactArchetype = new("88888888-8888-8888-8888-888888888888");
    public static readonly Guid DashboardArchetype = new("a15028e3-4e81-5543-8795-fa511b7487aa");
    public static readonly Guid Page = new("b7fa446c-294f-55d5-b9c1-e32743ccdf15");

    public static Guid SiteOf(string submissionId) => StableIdentity.Derive(submissionId, "site");

    public static Guid StudyOf(string submissionId) => StableIdentity.Derive(submissionId, "study");

    public static Guid ParcelOf(string submissionId) => StableIdentity.Derive(submissionId, "parcel");

    public static Guid ContactOf(string submissionId) => StableIdentity.Derive(submissionId, "contact");

    public static string AddressOn(string siteName) =>
        $"{siteName.Replace(" ", string.Empty).ToLowerInvariant()}@example.test";

    /// <summary>The spec the page carries: one figure read off the site, one read through the parcel, one
    /// verdict reached by walking `studies` backwards, and a provenance line walking the parcel's
    /// `obtainedBy`. Between them they exercise every shape a walk is written in.</summary>
    public const string PageSpec = """
        {"title": "Site submission",
         "compare": {"label": "site", "archetype": "Site"},
         "sections": [
           {"title": "The land", "widgets": [
             {"type": "kpi", "title": "Stated area",
              "value": {"kind": "property", "thing": "$scope", "property": "statedAreaHectares"},
              "origin": {"kind": "origin", "property": "statedAreaHectares",
                         "reads": {"stated": "as submitted", "unknown": "no origin recorded"},
                         "via": [{"predicate": "has", "archetype": "Parcel"}],
                         "source": {"via": [{"predicate": "obtainedBy"}]}}},
             {"type": "verdict", "title": "What the analysis says",
              "value": {"kind": "verdict", "via": [{"predicate": "studies", "direction": "in"}],
                        "states": [{"state": "EnergyNetPositive", "reads": "the site makes what it uses"}]}}]}]}
        """;

    /// <summary>The model a submitter's page is drawn from: the marks, the archetypes, one submitter's
    /// site with its parcel, study and contact — and a second submitter's, which is what a test about who
    /// may read what needs there to be.</summary>
    public static BrokerSnapshot WithTwoSubmissions(string submissionId = WillowBend.SubmissionId)
    {
        var snapshot = new BrokerSnapshot()
            .Thing(IsPredicate, SubmissionFragmentComposer.IsPredicateName)
            .Thing(HasPredicate, SubmissionFragmentComposer.HasPredicateName)
            .Thing(StudiesPredicate, SubmissionFragmentComposer.StudiesPredicateName)
            .Thing(ObtainedByPredicate, "obtainedBy")
            .Thing(SiteArchetype, SubmissionFragmentComposer.SiteArchetypeName, isArchetype: true)
            .Thing(StudyArchetype, SubmissionFragmentComposer.SiteStudyArchetypeName, isArchetype: true)
            .Thing(ParcelArchetype, SubmissionFragmentComposer.ParcelArchetypeName, isArchetype: true)
            .Thing(ContactArchetype, SubmissionFragmentComposer.ContactArchetypeName, isArchetype: true,
                properties: Flag(FindingsReader.PersonalDetailFlag))
            .Thing(DashboardArchetype, "Dashboard", isArchetype: true)
            .Thing(Page, "Site Submission Dashboard", properties: new Dictionary<string, object>
            {
                [FindingsReader.FindingsDashboardFlag] = new { typeInfo = "vos.Boolean", value = true },
                [FindingsReader.SpecProperty] = new { typeInfo = "vos.String", value = PageSpec },
            })
            .Relate(Page, IsPredicate, DashboardArchetype);

        return snapshot.WithSubmission(submissionId, "Willow Bend").WithSubmission(OtherSubmissionId, "Alder Rise");
    }

    private static Dictionary<string, object> Flag(string name) =>
        new() { [name] = new { typeInfo = "vos.Boolean", value = true } };

    private BrokerSnapshot WithSubmission(string submissionId, string siteName)
    {
        var site = SiteOf(submissionId);
        var study = StudyOf(submissionId);
        var parcel = ParcelOf(submissionId);
        var contact = ContactOf(submissionId);

        return Thing(site, siteName, properties: Stating(("statedAreaHectares", 24.0, "FactOnly")))
            .Thing(study, $"{siteName} Site Study", states: ["EnergyNetPositive"])
            .Thing(parcel, $"{siteName} Parcel-01", properties: Stating(("measuredAreaHectares", 23.6, null)))
            .Thing(contact, $"{siteName} Contact", properties: new Dictionary<string, object>
            {
                [FindingsReader.EmailAddressProperty] = new
                {
                    typeInfo = "vos.String",
                    value = AddressOn(siteName),
                },
            })
            .Relate(site, IsPredicate, SiteArchetype)
            .Relate(study, IsPredicate, StudyArchetype)
            .Relate(parcel, IsPredicate, ParcelArchetype)
            .Relate(contact, IsPredicate, ContactArchetype)
            .Relate(site, HasPredicate, parcel)
            .Relate(study, StudiesPredicate, site);
    }
}
