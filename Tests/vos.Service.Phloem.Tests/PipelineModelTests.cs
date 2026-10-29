using System.Text.Json;
using FluentAssertions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using Xunit;
using vos.Service.Shared.Roles;

namespace vos.Service.Phloem.Tests;

// DAG resolution + validation: build the executable DAG from the loaded graph and
// validate it acyclic with type-compatible wires.
public class PipelineModelTests
{
    [Fact] // Boundary nodes resolve with a kind + their own declared ports, and no subdomain to call.
    public void Build_BoundaryNodes_ResolveKindAndDeclaredPorts()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var input = dag.Node(fx.Get("In").Id)!;
        input.Kind.Should().Be(DagNodeKind.Input);
        input.Subdomain.Should().BeEmpty();
        input.OutputPorts.Select(p => p.PortName).Should().Contain("seed");

        var output = dag.Node(fx.Get("Out").Id)!;
        output.Kind.Should().Be(DagNodeKind.Output);
        output.Subdomain.Should().BeEmpty();
        output.Ports.Where(p => p.IsInput).Select(p => p.PortName).Should().Contain("result");

        // The service node in the same pipeline still resolves as a Service with a subdomain.
        dag.Node(fx.Get("Echo").Id)!.Kind.Should().Be(DagNodeKind.Service);

        // Both boundary wires are present (In.seed → Echo.message, Echo.echo → Out.result).
        dag.Wires.Should().HaveCount(2);
    }

    [Fact]
    public void Build_ATellNode_TellsTheSystemItStandsForThroughTheConnectionItNames_AndCallsNoService()
    {
        var telling = TestGraphs.TellingPipeline();

        var dag = PipelineDagBuilder.Build(telling.Fixture.Build(), telling.PipelineId);

        var tell = dag.Node(telling.Fixture.Get(TestGraphs.TellNode).Id)!;
        tell.Kind.Should().Be(DagNodeKind.Tell);
        tell.Tells.Should().Be(new Telling(telling.SystemId, "Reporting office", telling.ConnectionId));
        tell.Subdomain.Should().BeEmpty();
        dag.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Build_ATellNodeStandingForNoSystem_IsRefusedNamingIt()
    {
        var telling = TestGraphs.TellingPipeline();
        telling.Fixture.Unrelate(telling.Fixture.Get(TestGraphs.TellNode), telling.Fixture.Get("standsFor"), telling.Fixture.Get("Reporting office"));

        PipelineDagBuilder.Build(telling.Fixture.Build(), telling.PipelineId).Problems
            .Should().ContainMatch($"*'{TestGraphs.TellNode}' is a tell node but stands for no external system*");
    }

    [Fact]
    public void Build_AFollowOnNodeStandingForNoPipeline_IsRefusedNamingIt()
    {
        var starting = TestGraphs.StartingPipeline();
        starting.Fixture.Unrelate(starting.Fixture.Get(TestGraphs.FollowOnNode), starting.Fixture.Get("standsFor"), starting.Fixture.Get("What follows"));

        PipelineDagBuilder.Build(starting.Fixture.Build(), starting.PipelineId).Problems
            .Should().ContainMatch($"*'{TestGraphs.FollowOnNode}' is a follow-on node but stands for no pipeline*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_AnOutputNodeStandingForASystemOrAPipeline_IsRefusedNamingTheNodeThatDoesItNow(bool forASystem)
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var standsFor = fx.Playing("standsFor", PipelineRoles.StandsFor);
        var stoodFor = fx.Thing(forASystem ? "Reporting office" : "What follows");
        fx.Rel(stoodFor, fx.Get("is"), forASystem ? fx.Playing("Neighbour", PipelineRoles.ExternalSystem) : fx.Get("Pipeline"));
        fx.Rel(fx.Get("Out"), standsFor, stoodFor);

        PipelineDagBuilder.Build(fx.Build(), pipelineId).Problems.Should().ContainMatch(forASystem
            ? "*'Out' is the Output node and stands for 'Reporting office'; a tell node wired on to it tells a system*"
            : "*'Out' is the Output node and stands for 'What follows'; a follow-on node wired on to it starts a pipeline*");
    }

    [Fact]
    public void Build_ATellNodeStandingForASystemThatNamesNoConnection_IsRefusedNamingTheSystem()
    {
        var telling = TestGraphs.TellingPipeline(namesItsConnection: false);

        PipelineDagBuilder.Build(telling.Fixture.Build(), telling.PipelineId).Problems
            .Should().ContainMatch("*'Reporting office'*names no connection it is told through*");
    }

    [Fact] // A boundary pipeline validates: every wire connects real ports and the graph is acyclic.
    public void Validate_BoundaryPipeline_IsValid()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        DagValidator.Validate(dag).Should().BeEmpty();
    }

    [Fact]
    public void Build_WiresHeldAsThings_ResolveTheirPorts()
    {
        var (fx, pipelineId) = TestGraphs.ParallelWirePipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var generate = dag.Node(fx.Get("Generate").Id)!;
        var echo = dag.Node(fx.Get("Echo").Id)!;
        var intoEcho = dag.WiresInto(echo.NodeId).ToList();
        intoEcho.Should().OnlyContain(w => w.From.Node == generate);
        intoEcho.Select(w => (w.From.Port.PortName, w.To.Port.PortName)).Should()
            .BeEquivalentTo(new[] { ("echo", "message"), ("trace", "context") });
    }

    [Fact] // The model refuses a second relationship on one subject, predicate and target, so only wire Things allow this.
    public void Build_TwoWireThingsBetweenOneNodePair_YieldsBoth()
    {
        var (fx, pipelineId) = TestGraphs.ParallelWirePipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.WiresInto(fx.Get("Echo").Id).Should().HaveCount(2);
    }

    [Fact]
    public void Validate_ParallelWirePipeline_IsValid()
    {
        var (fx, pipelineId) = TestGraphs.ParallelWirePipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        DagValidator.Validate(dag).Should().BeEmpty();
    }

    [Fact]
    public void Build_ResolvesNodesSubdomainsPortsAndWire()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.Nodes.Select(node => node.Name).Should().BeEquivalentTo("Generate", "Echo", "Demo end");
        var generate = dag.Node(fx.Get("Generate").Id)!;
        var echo = dag.Node(fx.Get("Echo").Id)!;
        generate.Subdomain.Should().Be("gen");
        echo.Subdomain.Should().Be("ech");

        // ports resolve from the bound service's is-chain (proto)
        generate.OutPort("echo").Should().NotBeNull();
        InPortOf(echo, "message").Required.Should().BeTrue();

        var wire = dag.WiresOutOf(generate.NodeId).Single();
        wire.From.Should().Be(new WireEnd(generate, generate.OutPort("echo")!));
        wire.To.Should().Be(new WireEnd(echo, InPortOf(echo, "message")));
    }

    // The whole point of role Things: a model may call its archetypes anything, and a deployment that
    // renames them used to leave this orchestrator starting cleanly and finding nothing.
    [Fact]
    public void Build_WhenTheModelNamesEveryArchetypeSomethingElse_ResolvesTheSameDag()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary(namePrefix: "Renamed");
        var carries = fx.Thing("carries");
        fx.Rel(carries, vocabulary.Is, vocabulary.PipelineWire);

        var prototype = fx.Thing("proto");
        fx.Rel(prototype, vocabulary.Is, vocabulary.Service);
        var portOut = fx.Thing("p.out", ("direction", "out"), ("type", "string"), ("portName", "produced"));
        var portIn = fx.Thing("p.in", ("direction", "in"), ("type", "string"), ("portName", "consumed"));
        fx.Rel(portOut, vocabulary.Is, vocabulary.Port);
        fx.Rel(portIn, vocabulary.Is, vocabulary.Port);
        fx.Rel(prototype, vocabulary.Has, portOut);
        fx.Rel(prototype, vocabulary.Has, portIn);

        GraphThing Node(string name, string subdomain)
        {
            var service = fx.Thing($"{name}Service");
            fx.Rel(service, vocabulary.Is, prototype);
            var connection = fx.Thing($"{name}Connection", ("Subdomain", subdomain));
            fx.Rel(connection, vocabulary.Is, vocabulary.Connection);
            fx.Rel(connection, vocabulary.Has, service);
            var node = fx.Thing(name);
            fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
            fx.Rel(node, vocabulary.Has, connection);
            return node;
        }

        var first = Node("First", "one");
        var second = Node("Second", "two");
        var pipe = fx.Thing("Renamed pipeline");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Publish(pipe);
        fx.Rel(pipe, vocabulary.Has, first);
        fx.Rel(pipe, vocabulary.Has, second);
        fx.Wire(first, "produced", second, "consumed");
        fx.EndEveryPath(pipe);

        var dag = PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        dag.Nodes.Should().HaveCount(3);
        dag.Node(first.Id)!.Subdomain.Should().Be("one");
        dag.WiresOutOf(first.Id).Should().ContainSingle();
        DagValidator.Validate(dag).Should().BeEmpty();
    }

    // A model that binds nothing answers "no" to every question the orchestrator asks of it, so a pipeline
    // fully described in the model reads as one with nothing in it.
    [Fact]
    public void Build_WhenTheModelBindsNoRoles_SaysWhichRolesAreUnbound()
    {
        var fx = new GraphFixture();
        var isPredicate = fx.Thing("is");
        var pipelineArchetype = fx.Thing("Pipeline");
        var pipe = fx.Thing("P");
        fx.Rel(pipe, isPredicate, pipelineArchetype);

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>()
            .WithMessage("*The model binds no Thing to the roles*")
            .And.Message.Should().Contain(PipelineRoles.Pipeline.Word)
            .And.Contain(PipelineRoles.Port.Word);
    }

    // One role left unbound is refused by name rather than being read as "no node plays that role", which
    // is the silence this replaced.
    [Fact]
    public void Build_WhenOneRoleIsUnbound_NamesOnlyThatRole()
    {
        var fx = new GraphFixture();
        var isPredicate = fx.Thing("is");
        // Named after the role each plays, because in this test the name is the one thing that cannot matter.
        foreach (var role in PipelineRoles.DagRoles.Where(role => role != PipelineRoles.PipelineWire))
            fx.Playing(role.Word, role);
        var pipe = fx.Thing("P");
        fx.Rel(pipe, isPredicate, fx.Get(PipelineRoles.Pipeline.Word));

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>()
            .WithMessage($"*role {PipelineRoles.PipelineWire.Word}.*")
            .And.Message.Should().NotContain(PipelineRoles.Port.Word + ",");
    }

    // A role answers which one Thing does a job, so a second binding is a mistake to report, never a list to read.
    [Fact]
    public void Build_WhenARoleBindsTwoThings_RefusesNamingBoth()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var second = fx.Thing("SecondPipelineArchetype");
        fx.Rel(fx.Get($"{PipelineRoles.Pipeline.Word} role"), fx.Get(ModelNames.Binds), second);
        var pipe = fx.Thing("P");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Publish(pipe);

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>()
            .WithMessage($"*role {PipelineRoles.Pipeline.Word} binds 2 Things*")
            .And.Message.Should().Contain(vocabulary.Pipeline.Name).And.Contain(second.Name);
    }

    // Every model must derive the same identifier for a role, whatever language its seed is written in.
    [Fact]
    public void Each_role_identifier_is_the_standard_version_5_identifier_of_its_word()
    {
        var roles = typeof(PipelineRoles).GetFields().Where(field => field.FieldType == typeof(Role))
            .Select(field => (Role)field.GetValue(null)!).ToList();

        roles.Should().NotBeEmpty();
        foreach (var role in roles)
            role.Id.Should().Be(StandardVersion5(RoleIdentity.Namespace, role.Word), $"the role {role.Word}");
    }

    private static Guid StandardVersion5(Guid @namespace, string name)
    {
        var hash = System.Security.Cryptography.SHA1.HashData(
            [.. @namespace.ToByteArray(bigEndian: true), .. System.Text.Encoding.UTF8.GetBytes(name)]);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    [Fact]
    public void Build_WhenTheThingIsNotAPipeline_Refuses()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var notAPipeline = fx.Thing("Echo");
        fx.Rel(notAPipeline, vocabulary.Is, vocabulary.PipelineNode);

        var act = () => PipelineDagBuilder.Build(fx.Build(), notAPipeline.Id);

        act.Should().Throw<PipelineModelException>().WithMessage("*'Echo') is not a pipeline*");
    }

    [Fact]
    public void Build_WhenThePipelineHasNoNodes_Refuses()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var pipe = fx.Thing("Empty");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Publish(pipe);

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>().WithMessage("*'Empty' has no nodes*");
    }

    // Without a subdomain there is no address to call the node at, and a run would fail one node at a
    // time instead of refusing the pipeline.
    [Fact]
    public void Build_WhenTheConnectionCarriesNoSubdomain_Refuses()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var connection = fx.Thing("addressless");
        fx.Rel(connection, vocabulary.Is, vocabulary.Connection);
        var node = fx.Thing("Echo");
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(node, vocabulary.Has, connection);
        var pipe = fx.Thing("P");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Publish(pipe);
        fx.Rel(pipe, vocabulary.Has, node);

        PipelineDagBuilder.Build(fx.Build(), pipe.Id).Problems
            .Should().ContainMatch("*'addressless' for node 'Echo' has no Subdomain*");
    }

    // A node whose bindings are unreadable runs with none rather than failing the whole pipeline: the
    // run-param routing is optional, and a required input left unfilled is caught by validation instead.
    [Fact]
    public void Build_WhenParameterBindingsAreNotReadable_LeavesTheNodeWithNone()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var broken = fx.Thing("Broken", ("parameterBindings", "{not json"));
        fx.Rel(broken, fx.Get("is"), fx.Get("PipelineNode"));
        fx.Rel(broken, fx.Get("has"), fx.Get("echConn"));
        fx.Rel(fx.Get("Demo"), fx.Get("has"), broken);

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.Node(broken.Id)!.ParameterBindings.Should().BeEmpty();
    }

    // The platform keeps no fallback to a spelling it has moved past: a model still carrying the short form
    // binds nothing, and is reseeded.
    [Fact]
    public void Build_ReadsTheBindingPropertySpelledInFull_AndIgnoresTheShortForm()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var shortForm = fx.Thing("ShortForm", ("paramBindings", "{\"message\":\"greeting\"}"));
        fx.Rel(shortForm, fx.Get("is"), fx.Get("PipelineNode"));
        fx.Rel(shortForm, fx.Get("has"), fx.Get("echConn"));
        fx.Rel(fx.Get("Demo"), fx.Get("has"), shortForm);
        var full = fx.Thing("Full", ("parameterBindings", "{\"message\":\"greeting\"}"));
        fx.Rel(full, fx.Get("is"), fx.Get("PipelineNode"));
        fx.Rel(full, fx.Get("has"), fx.Get("echConn"));
        fx.Rel(fx.Get("Demo"), fx.Get("has"), full);

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.Node(shortForm.Id)!.ParameterBindings.Should().BeEmpty();
        dag.Node(full.Id)!.ParameterBindings.Should().Equal(new Dictionary<string, string> { ["message"] = "greeting" });
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(false, false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData(1, false)]
    public void Build_ReadsAPortMarkDeclaredAsABooleanOrAsTheWordAlike(object declared, bool expected)
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var port = fx.Thing("p.marked", ("direction", "in"), ("type", "string"), ("portName", "marked"),
            ("required", declared), ("collection", declared));
        fx.Rel(port, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get("EchoProto"), fx.Get("has"), port);

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var resolved = InPortOf(dag.Node(fx.Get("Echo").Id)!, "marked");
        resolved.Required.Should().Be(expected);
        resolved.Collection.Should().Be(expected);
    }

    [Fact]
    public void Build_ResolvesTheNodesOwnOutPortsBesideItsServices()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();
        var note = fx.Thing("decide.note", ("direction", "out"), ("type", "any"), ("portName", "note"));
        fx.Rel(note, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get("Decide"), fx.Get("has"), note);

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var decide = dag.Node(fx.Get("Decide").Id)!;
        decide.Ports.Select(p => p.PortName).Should().BeEquivalentTo("note", "in", "out", "error");
        decide.OutPort("note").Should().NotBeNull();
        DagValidator.Validate(dag).Should().BeEmpty();
    }

    [Fact]
    public void Build_ReadsTheChoicesTheNodeHolds_WithTheThingsEachComparesWith()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();

        var decide = PipelineDagBuilder.Build(fx.Build(), pipelineId).Node(fx.Get("Decide").Id)!;

        decide.DecisionPaths.Select(path => (path.Id, path.Name, Rule: path.When is not null, Things: path.Things.GetRawText())).Should().BeEquivalentTo(
        [
            (fx.Get("accept").Id, "accept", true, $"[{{\"id\":\"{fx.Get("approved").Id}\",\"name\":\"approved\"}}]"),
            (fx.Get("refuse").Id, "refuse", false, "[]"),
        ]);
    }

    [Fact]
    public void Build_ADecisionPathItsServiceHas_IsNoChoiceOfTheNode()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();
        var held = fx.Thing("defer");
        fx.Rel(held, fx.Get("is"), fx.Get("DecisionPath"));
        fx.Rel(fx.Get("DecideProto"), fx.Get("has"), held);

        var decide = PipelineDagBuilder.Build(fx.Build(), pipelineId).Node(fx.Get("Decide").Id)!;

        decide.DecisionPaths.Select(path => path.Name).Should().BeEquivalentTo("accept", "refuse");
    }

    [Fact]
    public void Build_ReadsWhetherAnOutPortProducesAThing()
    {
        var (fx, pipelineId) = TestGraphs.ProducingPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var maker = dag.Node(fx.Get("Maker").Id)!;
        maker.OutPort("made")!.Produces.Should().BeTrue();
        maker.OutPort("note")!.Produces.Should().BeFalse();
    }

    [Fact]
    public void Build_ReadsTheThingChosenOnTheNode_ForThePortOfferingIt()
    {
        var choice = TestGraphs.ChoicePipeline();
        choice.Fixture.Rel(choice.Node, choice.Offers, choice.Offered[2]);

        var dag = PipelineDagBuilder.Build(choice.Fixture.Build(), choice.PipelineId);

        dag.Node(choice.Node.Id)!.Choices.Should().Equal(new Dictionary<string, ChosenThing>
        {
            ["strategy"] = new(choice.Offered[2].Id, "Soonest first"),
        });
    }

    [Fact]
    public void Build_RefusesAChoiceNoPortOffers_AndTwoChoicesForOnePort()
    {
        var unoffered = TestGraphs.ChoicePipeline();
        unoffered.Fixture.Rel(unoffered.Node, unoffered.Offers, unoffered.Fixture.Thing("At random"));
        var twice = TestGraphs.ChoicePipeline();
        twice.Fixture.Rel(twice.Node, twice.Offers, twice.Offered[0]);
        twice.Fixture.Rel(twice.Node, twice.Offers, twice.Offered[1]);

        PipelineDagBuilder.Build(unoffered.Fixture.Build(), unoffered.PipelineId).Problems
            .Should().ContainMatch("*'Choose' chose 'At random', which no in port of it offers*");
        PipelineDagBuilder.Build(twice.Fixture.Build(), twice.PipelineId).Problems
            .Should().ContainMatch("*'Choose' chose two Things for its port 'strategy'*");
    }

    [Fact]
    public void Build_ReadsTheStateAWaitingNodeWaitsFor_AndTheInPortWhoseThingMustEnterIt()
    {
        var waiting = TestGraphs.WaitingPipeline();

        var dag = PipelineDagBuilder.Build(waiting.Fixture.Build(), waiting.PipelineId);

        dag.Node(waiting.Review.Id)!.Waits.Should().Be(new DagWait(waiting.Decided.Id, "decided", "submission"));
        dag.Node(waiting.Publish.Id)!.Waits.Should().BeNull();
    }

    [Fact]
    public void Build_RefusesAWaitingNodeNamingOnlyTheStateOrOnlyThePort()
    {
        var stateOnly = TestGraphs.WaitingPipeline();
        stateOnly.Fixture.Unrelate(stateOnly.Review, stateOnly.AwaitsThrough, stateOnly.Submission);
        var portOnly = TestGraphs.WaitingPipeline();
        portOnly.Fixture.Unrelate(portOnly.Review, portOnly.ProvenBy, portOnly.Decided);

        PipelineDagBuilder.Build(stateOnly.Fixture.Build(), stateOnly.PipelineId).Problems
            .Should().ContainMatch("*'Review' waits for 'decided' but names no in port whose Thing must enter it*");
        PipelineDagBuilder.Build(portOnly.Fixture.Build(), portOnly.PipelineId).Problems
            .Should().ContainMatch("*'Review' waits through its port 'submission' but names no state for it to enter*");
    }

    [Fact]
    public void Build_ReadsThePipelineAFollowOnNodeStandsFor()
    {
        var starting = TestGraphs.StartingPipeline();

        var dag = PipelineDagBuilder.Build(starting.Fixture.Build(), starting.PipelineId);

        var followOn = dag.Nodes.Single(node => node.Name == TestGraphs.FollowOnNode);
        followOn.Kind.Should().Be(DagNodeKind.FollowOn);
        followOn.Starts.Should().Be(new Starting(starting.NextPipelineId, "What follows"));
        followOn.Tells.Should().BeNull();
        dag.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Build_RefusesAFollowOnNodeStandingForThePipelineItIsIn()
    {
        var itself = TestGraphs.StartingPipeline(itself: true);

        PipelineDagBuilder.Build(itself.Fixture.Build(), itself.PipelineId).Problems
            .Should().ContainMatch($"*'{TestGraphs.FollowOnNode}' stands for 'BoundaryDemo', the pipeline it is in, so a run would start another of itself without end*");
    }

    [Fact]
    public void Build_ReadsANodesRetryPolicyWithTheOtherNumbersDefaulted_AndNoneWithoutAnAttemptsCount()
    {
        var withPolicy = TestGraphs.RetryPipeline(policy: [("attempts", 3), ("pauseMinutes", 2.5), ("pauseGrowth", 2), ("pauseSpread", 0.2), ("tryingMinutes", 90)]);
        var countOnly = TestGraphs.RetryPipeline(policy: [("attempts", 2)]);
        var without = TestGraphs.RetryPipeline();

        PipelineDagBuilder.Build(withPolicy.Fixture.Build(), withPolicy.PipelineId).Node(withPolicy.Echo.Id)!.Retries
            .Should().Be(new RetryPolicy(3, 2.5, 2, 0.2, 90));
        PipelineDagBuilder.Build(countOnly.Fixture.Build(), countOnly.PipelineId).Node(countOnly.Echo.Id)!.Retries
            .Should().Be(new RetryPolicy(2, 1, 1, 0, 24 * 60));
        PipelineDagBuilder.Build(without.Fixture.Build(), without.PipelineId).Node(without.Echo.Id)!.Retries.Should().BeNull();
    }

    [Fact]
    public void A_retry_pause_grows_by_its_multiplier_each_attempt_and_its_spread_adds_up_to_that_fraction()
    {
        var policy = new RetryPolicy(Attempts: 4, PauseMinutes: 2, PauseGrowth: 3, PauseSpread: 0.5, TryingMinutes: 60);

        policy.PauseBefore(2, spreadDraw: 0).Should().Be(TimeSpan.FromMinutes(2));
        policy.PauseBefore(3, spreadDraw: 0).Should().Be(TimeSpan.FromMinutes(6));
        policy.PauseBefore(3, spreadDraw: 1).Should().Be(TimeSpan.FromMinutes(9));
    }

    [Theory]
    [InlineData("attempts", 0, "allows 0 attempts")]
    [InlineData("pauseGrowth", 0.5, "a number out of range")]
    [InlineData("pauseMinutes", -1, "a number out of range")]
    [InlineData("tryingMinutes", 0, "a number out of range")]
    public void Build_RefusesARetryPolicyWithANumberOutOfRange(string name, double value, string reason)
    {
        var policy = name == "attempts"
            ? TestGraphs.RetryPipeline(policy: [("attempts", (int)value)])
            : TestGraphs.RetryPipeline(policy: [("attempts", 2), (name, value)]);

        PipelineDagBuilder.Build(policy.Fixture.Build(), policy.PipelineId).Problems.Should().ContainMatch($"*'Echo'*{reason}*");
    }

    [Fact]
    public void Build_ANodeAModelStillHandsOverAlong_IsReadWithoutAProblem()
    {
        var handsOverToItsOwn = TestGraphs.RetryPipeline(policy: [("attempts", 2)]);
        handsOverToItsOwn.Fixture.Rel(handsOverToItsOwn.Echo, handsOverToItsOwn.Fixture.Playing("handsOverTo", TestGraphs.RemovedHandOver),
            handsOverToItsOwn.Fixture.Get("RetryDemo"));

        var dag = PipelineDagBuilder.Build(handsOverToItsOwn.Fixture.Build(), handsOverToItsOwn.PipelineId);

        dag.Problems.Should().BeEmpty();
        DagValidator.Validate(dag).Should().BeEmpty();
    }

    [Fact]
    public void Build_RefusesAWaitingNodeWaitingThroughTwoPorts()
    {
        var twoPorts = TestGraphs.WaitingPipeline();
        twoPorts.Fixture.Rel(twoPorts.Review, twoPorts.AwaitsThrough, twoPorts.Fixture.Get("p.notice"));

        PipelineDagBuilder.Build(twoPorts.Fixture.Build(), twoPorts.PipelineId).Problems
            .Should().ContainMatch("*'Review' waits through two ports, so no one Thing could be waited for*");
    }

    [Fact]
    public void Build_RefusesAWaitThroughAPortThatIsNotAnInPortOfTheNode()
    {
        var outPort = TestGraphs.WaitingPipeline();
        outPort.Fixture.Unrelate(outPort.Review, outPort.AwaitsThrough, outPort.Submission);
        outPort.Fixture.Rel(outPort.Review, outPort.AwaitsThrough, outPort.Fixture.Get("p.notice"));
        var foreign = TestGraphs.WaitingPipeline();
        foreign.Fixture.Unrelate(foreign.Review, foreign.AwaitsThrough, foreign.Submission);
        foreign.Fixture.Rel(foreign.Review, foreign.AwaitsThrough, foreign.Fixture.Get("p.in"));

        PipelineDagBuilder.Build(outPort.Fixture.Build(), outPort.PipelineId).Problems
            .Should().ContainMatch("*'Review' waits through 'notice', which is not an in port of it*");
        PipelineDagBuilder.Build(foreign.Fixture.Build(), foreign.PipelineId).Problems
            .Should().ContainMatch("*'Review' waits through 'in', which is not an in port of it*");
    }

    [Fact]
    public void Build_RefusesANodeWaitingForTwoStates()
    {
        var waiting = TestGraphs.WaitingPipeline();
        waiting.Fixture.Rel(waiting.Review, waiting.ProvenBy, waiting.Fixture.Thing("withdrawn"));

        PipelineDagBuilder.Build(waiting.Fixture.Build(), waiting.PipelineId).Problems.Should().ContainMatch("*'Review' waits for two states*");
    }

    [Fact]
    public void Validate_ANodeWithAProblemInEachPartItReads_ListsEveryOneAndStillJudgesItsPorts()
    {
        var retry = TestGraphs.RetryPipeline(policy: [("attempts", 0)]);
        var fx = retry.Fixture;
        fx.Rel(retry.Echo, fx.Playing("offers", PipelineRoles.Choice), fx.Thing("At random"));
        fx.Rel(retry.Echo, fx.Playing("provenBy", PipelineRoles.CompletionRange), fx.Thing("decided"));
        RequiredPortNothingFills(fx, "EchoProto");

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), retry.PipelineId)).Should()
            .ContainMatch("*'Echo' chose 'At random'*")
            .And.ContainMatch("*'Echo' waits for 'decided' but names no in port*")
            .And.ContainMatch("*'Echo' allows 0 attempts*")
            .And.ContainMatch("*'Echo' needs its port 'context' filled*");
    }

    [Fact]
    public void Validate_ATellNodeStandingForASystemItCannotTell_IsListedAndItsPortsAreStillJudged()
    {
        var telling = TestGraphs.TellingPipeline(namesItsConnection: false);
        var fx = telling.Fixture;
        var unfilled = fx.Thing("Tell.context", ("direction", "in"), ("type", "string"), ("portName", "context"), ("required", "true"));
        fx.Rel(unfilled, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get(TestGraphs.TellNode), fx.Get("has"), unfilled);

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), telling.PipelineId)).Should()
            .ContainMatch("*'Reporting office'*names no connection*")
            .And.ContainMatch($"*'{TestGraphs.TellNode}' needs its port 'context' filled*");
    }

    [Fact]
    public void Validate_RefusesAWaitingNodeWhoseAwaitedPortNothingFills_AndOneRunningOncePerItem()
    {
        var unfilled = TestGraphs.WaitingPipeline(wired: false);
        var perItem = TestGraphs.WaitingPipeline(collection: true);

        var unfilledResult = DagValidator.Validate(PipelineDagBuilder.Build(unfilled.Fixture.Build(), unfilled.PipelineId));
        var perItemResult = DagValidator.Validate(PipelineDagBuilder.Build(perItem.Fixture.Build(), perItem.PipelineId));

        unfilledResult.Should().ContainSingle(error =>
            error == "'Review' waits for the Thing reaching its port 'submission', but nothing fills that port.");
        perItemResult.Should().ContainSingle(error =>
            error == "'Review' waits, but runs once for each item of 'submission'; a node that waits runs once.");
    }

    [Fact]
    public void Validate_AWaitingPipelineWhoseAwaitedPortIsWired_IsValid()
    {
        var waiting = TestGraphs.WaitingPipeline();

        DagValidator.Validate(PipelineDagBuilder.Build(waiting.Fixture.Build(), waiting.PipelineId)).Should().BeEmpty();
    }

    [Fact]
    public void Build_NodeWithoutConnection_IsListedAndLeftOut()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var pipe = fx.Thing("P");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        fx.Publish(pipe);
        var node = fx.Thing("Lonely");
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(pipe, vocabulary.Has, node); // node binds no connection

        var dag = PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        dag.Problems.Should().ContainMatch("*binds no service connection*");
        dag.Nodes.Should().BeEmpty("a node the build cannot read is left out rather than run half-read");
    }

    [Fact]
    public void Validate_DemoPipeline_HasNothingToRefuse()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should().BeEmpty();
    }

    [Fact]
    public void Validate_RequiredInputsNothingFillsOnTwoNodes_NamesBoth()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        RequiredPortNothingFills(fx, "SourceProto");
        RequiredPortNothingFills(fx, "EchoProto");

        var errors = DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId));

        errors.Should().ContainMatch("*'Generate' needs its port 'context' filled*")
            .And.ContainMatch("*'Echo' needs its port 'context' filled*");
    }

    [Fact]
    public void Validate_ARequiredInputFilledByAWireAParameterOrAChoice_IsNotRefused()
    {
        var (wired, wiredId) = TestGraphs.DemoPipeline();
        var (bound, boundId) = TestGraphs.ParamBoundPipeline();
        var chosen = TestGraphs.ChoicePipeline();
        var lane = chosen.Fixture.Thing("p.lane", ("direction", "in"), ("type", "any"), ("portName", "lane"), ("required", "true"));
        chosen.Fixture.Rel(lane, chosen.Fixture.Get("is"), chosen.Fixture.Get("Port"));
        chosen.Fixture.Rel(chosen.Fixture.Get("ChooseProto"), chosen.Fixture.Get("has"), lane);
        var leftLane = chosen.Fixture.Thing("Left lane");
        chosen.Fixture.Rel(lane, chosen.Offers, leftLane);
        chosen.Fixture.Rel(chosen.Node, chosen.Offers, leftLane);

        DagValidator.Validate(PipelineDagBuilder.Build(wired.Build(), wiredId)).Should().BeEmpty();
        DagValidator.Validate(PipelineDagBuilder.Build(bound.Build(), boundId)).Should().BeEmpty();
        DagValidator.Validate(PipelineDagBuilder.Build(chosen.Fixture.Build(), chosen.PipelineId)).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ANodesOwnPortOfTheSameName_IsJudgedInPlaceOfItsServicesRequiredOne()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        RequiredPortNothingFills(fx, "EchoProto");
        var own = fx.Thing("Echo.context", ("direction", "in"), ("type", "string"), ("portName", "context"));
        fx.Rel(own, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get("Echo"), fx.Get("has"), own);

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ANodesOwnCollectionPortOfTheSameNameAsItsServicesOne_IsOneCollectionInput()
    {
        var (fx, pipelineId) = TestGraphs.FanOutPipeline();
        var own = fx.Thing("Scorer.item", ("direction", "in"), ("type", "string"), ("portName", "item"), ("collection", "true"));
        fx.Rel(own, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get("Scorer"), fx.Get("has"), own);

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ANodeWithTwoCollectionInputs_IsRefusedNamingBoth()
    {
        var (fx, pipelineId) = TestGraphs.FanOutPipeline();
        var second = fx.Thing("p.batch", ("direction", "in"), ("type", "string"), ("portName", "batch"), ("collection", "true"));
        fx.Rel(second, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get("ScoreProto"), fx.Get("has"), second);

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId))
            .Should().ContainMatch("*'Scorer' has 2 collection inputs ('item', 'batch')*");
    }

    [Fact]
    public void Validate_AProblemOnEachOfTwoNodes_ListsBoth()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        fx.Unrelate(fx.Get("genConn"), fx.Get("has"), fx.Get("genSvc"));
        var addressless = fx.Thing("addressless");
        fx.Rel(addressless, fx.Get("is"), fx.Get("PlatformServiceConnection"));
        var broken = fx.Thing("Broken");
        fx.Rel(broken, fx.Get("is"), fx.Get("PipelineNode"));
        fx.Rel(broken, fx.Get("has"), addressless);
        fx.Rel(fx.Get("Demo"), fx.Get("has"), broken);

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId))
            .Should().ContainMatch("*'genConn' binds no service*")
            .And.ContainMatch("*'addressless' for node 'Broken' has no Subdomain*");
    }

    [Fact]
    public void UnfilledByParameters_ARequiredPortBoundToAParameterTheRunWasNotGiven_NamesTheParameter()
    {
        var (fx, pipelineId) = TestGraphs.ParamBoundPipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        DagValidator.UnfilledByParameters(dag, JsonSerializer.SerializeToElement(new { farewell = "bye" }))
            .Should().ContainSingle().Which.Should().Contain("'Echo'").And.Contain("'message'").And.Contain("'greeting'");
        DagValidator.UnfilledByParameters(dag, default).Should().ContainSingle();
        DagValidator.UnfilledByParameters(dag, JsonSerializer.SerializeToElement(new { greeting = "hi" })).Should().BeEmpty();
    }

    [Fact]
    public void UnfilledByParameters_ARequiredPortAlsoWiredOrChosen_IsNotJudgedByTheParameter()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var echo = fx.Get("Echo");
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);
        InPortOf(dag.Node(echo.Id)!, "message").Required.Should().BeTrue();
        PipelineDag BoundToAnAbsentParameter(IReadOnlyList<DagWire> wires, IReadOnlyDictionary<string, ChosenThing> choices) => new()
        {
            PipelineId = dag.PipelineId,
            Nodes = dag.Nodes.Select(node => node.NodeId != echo.Id ? node : new DagNode
            {
                NodeId = node.NodeId, Name = node.Name, Subdomain = node.Subdomain, Ports = node.Ports,
                ParameterBindings = new Dictionary<string, string> { ["message"] = "absent" },
                Choices = choices,
            }).ToList(),
            Wires = wires,
        };
        var unwired = dag.Wires.Where(wire => wire.To.Node.NodeId != echo.Id).ToList();
        var chosen = new Dictionary<string, ChosenThing> { ["message"] = new(Guid.NewGuid(), "Hello") };

        DagValidator.UnfilledByParameters(BoundToAnAbsentParameter(dag.Wires, new Dictionary<string, ChosenThing>()), default).Should().BeEmpty();
        DagValidator.UnfilledByParameters(BoundToAnAbsentParameter(unwired, chosen), default).Should().BeEmpty();
        DagValidator.UnfilledByParameters(BoundToAnAbsentParameter(unwired, new Dictionary<string, ChosenThing>()), default)
            .Should().ContainSingle("the same port with neither a wire nor a choice is judged by its parameter");
    }

    private static DagPort InPortOf(DagNode node, string portName) => node.Ports.Single(port => port.IsInput && port.IsNamed(portName));

    private static void RequiredPortNothingFills(GraphFixture fx, string prototypeName)
    {
        var port = fx.Thing($"{prototypeName}.context", ("direction", "in"), ("type", "string"), ("portName", "context"), ("required", "true"));
        fx.Rel(port, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get(prototypeName), fx.Get("has"), port);
    }

    [Fact]
    public void Validate_Cycle_IsRejected()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipelineInACycle();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        DagValidator.Validate(dag).Should().Contain(e => e.Contains("cycle"));
    }

    [Fact]
    public void Validate_AWireFromAnOutPortWhoseServiceAlsoHasAnInPortOfTheSameName_IsValid()
    {
        var (fx, pipelineId) = TestGraphs.PassThroughPipeline();

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should().BeEmpty();
    }

    [Fact]
    public void Build_AWireIsFoundByThePortsItRelatesTo_SoRenamingBothPortsKeepsTheWire()
    {
        var (fx, pipelineId) = TestGraphs.PassThroughPipeline();
        fx.State(fx.Get("transform.text.out"), ModelNames.PortName, "shouted");
        fx.State(fx.Get("transform.text.in"), ModelNames.PortName, "words");

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var intoReverse = dag.WiresInto(fx.Get("Reverse").Id).Single();
        (intoReverse.From.Port.PortName, intoReverse.To.Port.PortName).Should().Be(("shouted", "words"));
        DagValidator.Validate(dag).Should().BeEmpty();
    }

    [Fact]
    public void Build_AWireCarryingOnlyPortNames_IsRefusedForTakingItsValueFromNoPort()
    {
        var (fx, pipelineId) = TestGraphs.PassThroughPipeline();
        var reverse = fx.Get("Reverse");
        fx.WireBetween(fx.Get("Uppercase"), null, reverse, fx.Get("transform.text.in"), ("fromPort", "text"), ("toPort", "text"));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should()
            .Contain("The wire from 'Uppercase' to 'Reverse' takes its value from no port: it relates to none along inputConnector.");
    }

    [Fact]
    public void Build_AWireTakingItsValueFromAnInPortOfItsNode_IsRefusedNamingThePort()
    {
        var (fx, pipelineId) = TestGraphs.PassThroughPipeline();
        fx.WireBetween(fx.Get("Uppercase"), fx.Get("transform.text.in"), fx.Get("Reverse"), fx.Get("transform.text.in"));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should()
            .Contain("The wire from 'Uppercase' to 'Reverse' takes its value from 'transform.text.in', which is not an out port of 'Uppercase'.");
    }

    [Fact]
    public void Build_AWireDeliveringToAnOutPortOfItsTarget_IsRefusedNamingThePort()
    {
        var (fx, pipelineId) = TestGraphs.PassThroughPipeline();
        fx.WireBetween(fx.Get("Uppercase"), fx.Get("transform.text.out"), fx.Get("Reverse"), fx.Get("transform.text.out"));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should()
            .Contain("The wire from 'Uppercase' to 'Reverse' delivers its value to 'transform.text.out', which is not an in port of 'Reverse'.");
    }

    [Fact]
    public void Build_AWireDeliveringToAPortOfAnotherNode_IsRefusedNamingThePort()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var echo = fx.Get("Echo");
        fx.WireBetween(fx.Get("Generate"), fx.PortOf(fx.Get("Generate"), "echo", "out"), echo, fx.Get("s.out"));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should()
            .Contain("The wire from 'Generate' to 'Echo' delivers its value to 's.out', which is not an in port of 'Echo'.");
    }

    [Fact]
    public void Build_AWireRelatedToNoInPort_IsRefused()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        fx.WireBetween(fx.Get("Generate"), fx.Get("s.out"), fx.Get("Echo"), null);

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should()
            .Contain("The wire from 'Generate' to 'Echo' delivers its value to no port: it relates to none along outputConnector.");
    }

    [Fact]
    public void Build_AWireRelatedToTwoOutPorts_IsRefused()
    {
        var (fx, pipelineId) = TestGraphs.ParallelWirePipeline();
        var generate = fx.Get("Generate");
        var wire = fx.WireBetween(generate, fx.PortOf(generate, "echo", "out"), fx.Get("Echo"), fx.PortOf(fx.Get("Echo"), "context", "in"));
        fx.Rel(wire, fx.Get("inputConnector"), fx.PortOf(generate, "trace", "out"));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should()
            .Contain("The wire from 'Generate' to 'Echo' relates to 2 ports along inputConnector; a wire takes its value from one.");
    }

    [Fact]
    public void Build_AWireDrawnAsARelationshipBetweenTwoNodes_IsRefused()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        fx.Rel(fx.Get("Echo"), fx.Get("carries"), fx.Get("Generate"));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should()
            .Contain("The wire from 'Echo' to 'Generate' is drawn as a relationship between the two nodes; a wire is a Thing the node it leaves has, related to the two ports it connects.");
    }
}
