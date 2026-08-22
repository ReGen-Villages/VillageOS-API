using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

/// <summary>
/// `submissions` — what has arrived, and what a reviewer does with it (VillageOS #6045, #6621).
///
/// The model these read is built from marks rather than names, so every fixture spells its archetypes and
/// predicates differently from the shipped template. A handler that answered only to the shipped spelling
/// would pass against a fixture that copied it and fail against a project that renamed one.
/// </summary>
public class SubmissionsCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _mycelium = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private static readonly Guid ProposesId = new("11111111-0000-0000-0000-000000000001");
    private static readonly Guid ResolvedAsId = new("11111111-0000-0000-0000-000000000002");
    private static readonly Guid IsId = new("11111111-0000-0000-0000-000000000003");
    private static readonly Guid DispositionArchetypeId = new("11111111-0000-0000-0000-000000000004");
    private static readonly Guid RejectedId = new("11111111-0000-0000-0000-000000000005");
    private static readonly Guid PromotedId = new("11111111-0000-0000-0000-000000000006");
    private static readonly Guid SubmissionId = new("11111111-0000-0000-0000-000000000007");
    private static readonly Guid SiteId = new("11111111-0000-0000-0000-000000000008");

    private static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static JsonElement Json(object shape) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(shape));

    private static object Thing(Guid id, string name, bool isArchetype = false) =>
        new { Id = id, Name = name, IsArchetype = isArchetype };

    private static object Edge(Guid subject, Guid predicate, Guid target) =>
        new { Id = Guid.NewGuid(), SubjectId = subject, PredicateId = predicate, TargetId = target };

    private static object Held(object value) => new { Value = value, IsInherited = false };

    /// <summary>A model holding one submission. Its predicates and its disposition archetype are named
    /// nothing like the shipped template's, because a reader finds them by their marks.</summary>
    private void AModelWithOneSubmission(Guid? disposition = null)
    {
        _mycelium.Setup(client => client.GetAllThingsAsync()).ReturnsAsync(Json(new[]
        {
            Thing(ProposesId, "puts-forward"),
            Thing(ResolvedAsId, "decided"),
            Thing(IsId, "is"),
            Thing(DispositionArchetypeId, "Verdict", isArchetype: true),
            Thing(RejectedId, "binned"),
            Thing(PromotedId, "taken-on"),
            Thing(SubmissionId, "Willow Bend Submission"),
            Thing(SiteId, "Willow Bend"),
        }));

        var edges = new List<object>
        {
            Edge(SubmissionId, ProposesId, SiteId),
            Edge(RejectedId, IsId, DispositionArchetypeId),
            Edge(PromotedId, IsId, DispositionArchetypeId),
        };
        if (disposition is { } decided) edges.Add(Edge(SubmissionId, ResolvedAsId, decided));
        _mycelium.Setup(client => client.GetAllRelationshipsAsync()).ReturnsAsync(Json(edges));

        _mycelium.Setup(client => client.GetAllPropertiesAsync(It.IsAny<string>())).ReturnsAsync(Json(
            new Dictionary<string, object>
            {
                [ProposesId.ToString()] = new Dictionary<string, object>
                    { ["__IsProposedSitePredicate"] = Held(true) },
                [ResolvedAsId.ToString()] = new Dictionary<string, object>
                    { ["__IsSubmissionDispositionPredicate"] = Held(true) },
                [DispositionArchetypeId.ToString()] = new Dictionary<string, object>
                    { ["__IsSubmissionDispositionArchetype"] = Held(true) },
                [RejectedId.ToString()] = new Dictionary<string, object>
                    { ["daysBeforeColdStorage"] = Held(30) },
                [SubmissionId.ToString()] = new Dictionary<string, object>
                {
                    ["submissionId"] = Held("willow-bend-2026-08"),
                    ["submittedAt"] = Held("2026-08-22T09:30:00Z"),
                },
            }));
    }

    private async Task Run(string arg) =>
        await new SubmissionsCommandHandler(arg, _writer, _mycelium.Object).ExecuteAsync();

    [Fact]
    public async Task With_no_mycelium_it_shows_what_it_can_do()
    {
        await new SubmissionsCommandHandler("list", _writer, client: null).ExecuteAsync();

        Assert.Contains("Submission review commands:", _writer.ToString());
    }

    [Fact]
    public async Task A_submission_nobody_has_decided_about_is_listed_as_waiting()
    {
        AModelWithOneSubmission();

        await Run("list");

        var output = _writer.ToString();
        Assert.Contains("willow-bend-2026-08", output);
        Assert.Contains("2026-08-22T09:30:00Z", output);
        Assert.Contains("waiting", output);
        Assert.Contains("Willow Bend", output);
    }

    // The disposition is displayed as the model spells it, not as this tool would.
    [Fact]
    public async Task A_submission_that_has_been_decided_about_is_listed_as_that_decision()
    {
        AModelWithOneSubmission(disposition: RejectedId);

        await Run("list");

        var output = _writer.ToString();
        Assert.Contains("binned", output);
        Assert.DoesNotContain("waiting", output);
    }

    [Fact]
    public async Task A_model_holding_no_submissions_says_so()
    {
        _mycelium.Setup(client => client.GetAllThingsAsync()).ReturnsAsync(Parse("[]"));
        _mycelium.Setup(client => client.GetAllRelationshipsAsync()).ReturnsAsync(Parse("[]"));
        _mycelium.Setup(client => client.GetAllPropertiesAsync(It.IsAny<string>())).ReturnsAsync(Parse("{}"));

        await Run("list");

        Assert.Contains("No submissions in this model.", _writer.ToString());
    }

    // Which disposition is disposable is read off the model: the one naming a period after which a
    // submission goes. A tool holding the word "rejected" would answer nothing in a model that renamed it.
    [Fact]
    public async Task Rejecting_relates_the_submission_to_the_disposition_that_names_a_period()
    {
        AModelWithOneSubmission();

        await Run($"reject {SubmissionId}");

        _mycelium.Verify(client => client.CreateRelationshipAsync(SubmissionId, ResolvedAsId, RejectedId), Times.Once);
        Assert.Contains("binned", _writer.ToString());
    }

    [Fact]
    public async Task Rejecting_records_when_it_was_decided()
    {
        AModelWithOneSubmission();

        await Run($"reject {SubmissionId}");

        _mycelium.Verify(client => client.SetPropertyAsync(
            SubmissionId, "resolvedAt", "vos.DateTime", It.IsAny<object?>()), Times.Once);
    }

    [Fact]
    public async Task A_submission_can_be_named_by_the_identifier_it_was_submitted_under()
    {
        AModelWithOneSubmission();

        await Run("reject willow-bend-2026-08");

        _mycelium.Verify(client => client.CreateRelationshipAsync(SubmissionId, ResolvedAsId, RejectedId), Times.Once);
    }

    [Fact]
    public async Task A_name_no_submission_answers_to_is_refused_without_writing()
    {
        AModelWithOneSubmission();

        await Run("reject nothing-by-that-name");

        Assert.Contains("No submission here answers to", _writer.ToString());
        _mycelium.Verify(client => client.CreateRelationshipAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    // Thing names are not unique, so a name can answer twice. Picking either would reject or promote a
    // submission the reviewer did not name.
    [Fact]
    public async Task A_name_two_submissions_answer_to_is_refused_without_writing()
    {
        var second = new Guid("11111111-0000-0000-0000-000000000009");
        AModelWithOneSubmission();
        _mycelium.Setup(client => client.GetAllThingsAsync()).ReturnsAsync(Json(new[]
        {
            Thing(ProposesId, "puts-forward"),
            Thing(ResolvedAsId, "decided"),
            Thing(IsId, "is"),
            Thing(DispositionArchetypeId, "Verdict", isArchetype: true),
            Thing(RejectedId, "binned"),
            Thing(SubmissionId, "Willow Bend Submission"),
            Thing(second, "Willow Bend Submission"),
            Thing(SiteId, "Willow Bend"),
        }));
        _mycelium.Setup(client => client.GetAllRelationshipsAsync()).ReturnsAsync(Json(new[]
        {
            Edge(SubmissionId, ProposesId, SiteId),
            Edge(second, ProposesId, SiteId),
            Edge(RejectedId, IsId, DispositionArchetypeId),
        }));

        await Run("reject Willow Bend Submission");

        Assert.Contains("does not say which", _writer.ToString());
        _mycelium.Verify(client => client.CreateRelationshipAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    // Promotion walks from the site, not from the record of the arrival: the record proposes the site and
    // is deliberately not part of what travels.
    [Fact]
    public async Task Promoting_carries_the_site_the_submission_proposes()
    {
        AModelWithOneSubmission();
        _mycelium.Setup(client => client.PromoteAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Parse("""{"modelId":"22222222-0000-0000-0000-000000000001"}"""));

        await Run($"promote {SubmissionId} project.seed.json has,studies Willow Bend");

        _mycelium.Verify(client => client.PromoteAsync(
            SiteId, It.Is<IReadOnlyList<string>>(followed => followed.Contains("has")),
            "project.seed.json", "Willow Bend"), Times.Once);
    }

    [Fact]
    public async Task Promoting_marks_the_submission_with_the_disposition_that_names_no_period()
    {
        AModelWithOneSubmission();
        _mycelium.Setup(client => client.PromoteAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Parse("""{"modelId":"22222222-0000-0000-0000-000000000001"}"""));

        await Run($"promote {SubmissionId} project.seed.json has,studies Willow Bend");

        _mycelium.Verify(client => client.CreateRelationshipAsync(SubmissionId, ResolvedAsId, PromotedId), Times.Once);
    }

    [Fact]
    public async Task The_predicates_a_promotion_follows_can_be_named()
    {
        AModelWithOneSubmission();
        _mycelium.Setup(client => client.PromoteAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(Parse("{}"));

        await Run($"promote {SubmissionId} project.seed.json holds,examines Willow Bend");

        _mycelium.Verify(client => client.PromoteAsync(
            SiteId,
            It.Is<IReadOnlyList<string>>(followed =>
                followed.Contains("holds") && followed.Contains("examines") && followed.Count == 2),
            It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Promoting_without_a_template_and_a_name_shows_what_it_needs()
    {
        AModelWithOneSubmission();

        await Run($"promote {SubmissionId}");

        Assert.Contains("Usage: submissions promote", _writer.ToString());
        _mycelium.Verify(client => client.PromoteAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // A model that never declared the vocabulary answers with no submissions rather than throwing, because
    // an operator pointed at the wrong model needs to be told that, not given a stack trace.
    [Fact]
    public async Task A_model_marking_no_predicate_lists_nothing()
    {
        _mycelium.Setup(client => client.GetAllThingsAsync())
            .ReturnsAsync(Parse($$"""[{"Id":"{{SubmissionId}}","Name":"Something"}]"""));
        _mycelium.Setup(client => client.GetAllRelationshipsAsync()).ReturnsAsync(Parse("[]"));
        _mycelium.Setup(client => client.GetAllPropertiesAsync(It.IsAny<string>())).ReturnsAsync(Parse("{}"));

        await Run("list");

        Assert.Contains("No submissions in this model.", _writer.ToString());
    }
}
