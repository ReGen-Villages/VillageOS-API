using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using vos.Mcp.Tools.Process;
using vos.Tests.Shared;
using Xunit;

namespace vos.Mcp.Tests;

public class ServiceCallsTests
{
    private static readonly Guid AwaitsPredicate = Guid.Parse("11111111-1111-5111-8111-111111111111");
    private static readonly Guid WatchingThing = Guid.Parse("22222222-2222-5222-8222-222222222222");

    private readonly SelectionFromASeed _model = new(TestSeeds.Catchments);

    private Guid Catchment => _model.IdOf("Ålvik south slope");
    private Guid SurveysCatchment => _model.IdOf("surveysCatchment");
    private Guid Echo => _model.IdOf("echo");
    private Guid HandledBy => _model.IdOf("handledBy");

    [Fact]
    public void A_call_started_by_a_state_names_its_connection_and_service_and_reads_its_stamps()
    {
        var record = _model.Relate(Catchment, HandledBy, SurveysCatchment);
        var stamped = Stamped(record, """
            { "__CallState": "Failed", "__CallAttempts": 3, "__CallLastAttemptAt": "2026-11-18T10:00:00Z",
              "__CallLastError": "the service answered 500", "__CallRecordState": "CatchmentAwaitingSurvey",
              "__CallRetryReason": "the service was restarted" }
            """);

        var call = ServiceCalls.On(Catchment, _model.Selection(), stamped).Calls.Should().ContainSingle().Subject;

        call.Should().BeEquivalentTo(new
        {
            Record = record,
            Connection = new NamedThing(SurveysCatchment, "surveysCatchment"),
            Service = new NamedThing(_model.IdOf("Echo service"), "Echo service"),
            StartedBy = ServiceCalls.StartedByAState,
            KeptAcrossARestart = true,
            EnteredState = "CatchmentAwaitingSurvey",
            State = "Failed",
            StillGoing = false,
            CanBeRetried = true,
            Attempts = 3L,
            LastAttemptAt = "2026-11-18T10:00:00Z",
            LastError = "the service answered 500",
            LastRetryReason = "the service was restarted",
        });
    }

    [Fact]
    public void A_call_started_by_a_relationship_is_listed_and_says_its_record_is_not_kept()
    {
        var handled = _model.Relate(Catchment, predicate: Echo, _model.IdOf("North pond"));
        var stamped = Stamped(handled, """{ "__CallState": "Sent" }""");

        var call = ServiceCalls.On(Catchment, _model.Selection(), stamped).Calls.Should().ContainSingle().Subject;

        call.Connection.Name.Should().Be("echo");
        call.StartedBy.Should().Be(ServiceCalls.StartedByARelationship);
        call.KeptAcrossARestart.Should().BeFalse();
        call.StillGoing.Should().BeTrue();
        call.CanBeRetried.Should().BeFalse();
    }

    [Fact]
    public void A_call_the_server_holds_no_stamp_for_has_no_state_and_is_never_read_as_completed()
    {
        var handled = _model.Relate(Catchment, predicate: Echo, _model.IdOf("North pond"));

        var call = ServiceCalls.On(Catchment, _model.Selection(), Stamped(handled, "{}")).Calls.Should().ContainSingle().Subject;

        call.State.Should().BeNull();
        call.StillGoing.Should().BeFalse();
        call.CanBeRetried.Should().BeFalse();
    }

    [Fact]
    public void A_relationship_whose_predicate_is_no_connection_is_not_a_call()
    {
        ServiceCalls.On(_model.IdOf("Ålvik north slope"), _model.Selection(), Nothing).Calls.Should().BeEmpty(
            "the catchment has reservoirs along a predicate that calls nothing");
    }

    [Fact]
    public void Only_the_subject_of_a_relationship_was_worked_on()
    {
        _model.Relate(Catchment, predicate: Echo, _model.IdOf("North pond"));

        ServiceCalls.On(_model.IdOf("North pond"), _model.Selection(), Nothing).Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_record_whose_target_names_a_connection_along_the_bound_predicate_is_answered_through_it()
    {
        var notifies = _model.IdOf("notifies");
        var watcher = _model.Add(WatchingThing, "Survey watch");
        _model.Relate(watcher, notifies, SurveysCatchment);
        _model.Relate(Catchment, HandledBy, watcher);

        ServiceCalls.On(Catchment, _model.Selection(), Nothing).Calls.Should().ContainSingle()
            .Which.Connection.Name.Should().Be("surveysCatchment");
    }

    [Fact]
    public void The_Things_waiting_for_this_one_are_listed_with_the_connection_each_is_answered_through()
    {
        var awaits = _model.Add(AwaitsPredicate, "awaits");
        _model.Relate(ServiceCalls.AwaitedThingRole, _model.IdOf("binds"), awaits);
        var watcher = _model.Add(WatchingThing, "Survey watch");
        _model.Relate(watcher, awaits, Catchment);
        _model.Relate(watcher, _model.IdOf("notifies"), SurveysCatchment);

        ServiceCalls.On(Catchment, _model.Selection(), Nothing).WaitingForThisThing.Should().BeEquivalentTo(
            [new WaitingThing(watcher, "Survey watch", new NamedThing(SurveysCatchment, "surveysCatchment"))]);
    }

    [Fact]
    public void The_other_connections_that_watched_are_named_and_the_one_called_is_not_repeated()
    {
        var record = _model.Relate(Catchment, HandledBy, SurveysCatchment);
        var stamped = Stamped(record, $$"""{ "__CallWatchingConnections": "{{SurveysCatchment}},{{Echo}}" }""");

        ServiceCalls.On(Catchment, _model.Selection(), stamped).Calls.Single().AlsoWatching
            .Should().Equal(new NamedThing(Echo, "echo"));
    }

    [Fact]
    public void Calls_are_ordered_by_their_last_attempt_and_one_with_no_time_comes_last()
    {
        var undated = _model.Relate(Catchment, predicate: Echo, _model.IdOf("North pond"));
        var later = _model.Relate(Catchment, HandledBy, SurveysCatchment);
        var earlier = _model.Relate(Catchment, predicate: Echo, _model.IdOf("South pond"));
        var stamped = new Dictionary<Guid, JsonElement>
        {
            [undated] = Relationship("{}"),
            [later] = Relationship("""{ "__CallLastAttemptAt": "2026-11-18T10:00:00Z" }"""),
            [earlier] = Relationship("""{ "__CallLastAttemptAt": "2026-11-17T10:00:00Z" }"""),
        };

        ServiceCalls.On(Catchment, _model.Selection(), stamped).Calls.Select(call => call.Record)
            .Should().Equal(earlier, later, undated);
    }

    [Fact]
    public void A_model_that_binds_no_connection_kind_lists_no_call()
    {
        _model.Relate(Catchment, HandledBy, SurveysCatchment);
        _model.Unbind(ServiceCalls.ConnectionRole);

        ServiceCalls.On(Catchment, _model.Selection(), Nothing).Calls.Should().BeEmpty();
    }

    private static readonly IReadOnlyDictionary<Guid, JsonElement> Nothing = new Dictionary<Guid, JsonElement>();

    private static Dictionary<Guid, JsonElement> Stamped(Guid relationship, string properties) =>
        new() { [relationship] = Relationship(properties) };

    private static JsonElement Relationship(string properties) =>
        JsonDocument.Parse($$"""{ "Properties": {{properties}} }""").RootElement;
}

// A seed in the shape Mycelium answers a selection with, which a test adds Things and relationships to.
public sealed class SelectionFromASeed
{
    private readonly JsonArray _things = [];
    private readonly JsonArray _relationships = [];
    private int _added;

    public SelectionFromASeed(string seedPath)
    {
        var seed = JsonNode.Parse(File.ReadAllText(seedPath))!;
        foreach (var thing in seed["Things"]!.AsArray())
            _things.Add(new JsonObject { ["Id"] = thing!["Id"]!.GetValue<string>(), ["Name"] = thing["Name"]!.GetValue<string>() });
        foreach (var relationship in seed["Relationships"]!.AsArray())
            _relationships.Add(new JsonObject
            {
                ["Id"] = relationship!["Id"]!.GetValue<string>(),
                ["SubjectId"] = relationship["Subject"]!.GetValue<string>(),
                ["PredicateId"] = relationship["Predicate"]!.GetValue<string>(),
                ["TargetId"] = relationship["Target"]!.GetValue<string>(),
                ["Properties"] = new JsonObject(),
            });
    }

    public Guid IdOf(string name) =>
        Guid.Parse(_things.Single(thing => thing!["Name"]!.GetValue<string>() == name)!["Id"]!.GetValue<string>());

    public Guid Add(Guid id, string name)
    {
        _things.Add(new JsonObject { ["Id"] = id.ToString(), ["Name"] = name });
        return id;
    }

    public Guid Relate(Guid subject, Guid predicate, Guid target)
    {
        var id = new Guid(++_added, 0, 0, new byte[8]);
        _relationships.Add(new JsonObject
        {
            ["Id"] = id.ToString(), ["SubjectId"] = subject.ToString(), ["PredicateId"] = predicate.ToString(),
            ["TargetId"] = target.ToString(), ["Properties"] = new JsonObject(),
        });
        return id;
    }

    public void Unbind(Guid role)
    {
        var binds = IdOf("binds").ToString();
        foreach (var binding in _relationships
                     .Where(each => each!["SubjectId"]!.GetValue<string>() == role.ToString()
                                    && each["PredicateId"]!.GetValue<string>() == binds)
                     .ToList())
            _relationships.Remove(binding);
    }

    // As Mycelium answers: every relationship touching a carried Thing comes, whether or not its far end does.
    public JsonElement Selection(IReadOnlyCollection<string>? keepingOnly = null)
    {
        var things = _things.Where(thing => keepingOnly is null || keepingOnly.Contains(thing!["Name"]!.GetValue<string>())).ToList();
        var carried = things.Select(thing => thing!["Id"]!.GetValue<string>()).ToHashSet();
        var relationships = _relationships.Where(each =>
            carried.Contains(each!["SubjectId"]!.GetValue<string>()) || carried.Contains(each["TargetId"]!.GetValue<string>()));
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["things"] = new JsonArray([.. things.Select(thing => thing!.DeepClone())]),
            ["relationships"] = new JsonArray([.. relationships.Select(each => each!.DeepClone())]),
        });
    }
}
