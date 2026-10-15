using FluentAssertions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using Xunit;

namespace vos.Service.Phloem.Tests;

// DAG resolution + validation: build the executable DAG from the loaded graph and
// validate it acyclic with type-compatible wires.
public class PipelineModelTests
{
    [Fact] // Boundary nodes resolve with a kind + their own declared ports, and no dispatch subdomain.
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
    public void Build_AnEndStandingForAnExternalSystem_TellsItThroughTheConnectionItNames()
    {
        var telling = TestGraphs.TellingPipeline();

        var dag = PipelineDagBuilder.Build(telling.Fixture.Build(), telling.PipelineId);

        dag.Node(telling.Fixture.Get("Out").Id)!.Tells
            .Should().Be(new Telling(telling.SystemId, "Reporting office", telling.ConnectionId));
    }

    [Fact]
    public void Build_AnEndStandingForNothing_TellsNobody()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();

        PipelineDagBuilder.Build(fx.Build(), pipelineId).Node(fx.Get("Out").Id)!.Tells.Should().BeNull();
    }

    [Fact]
    public void Build_AnEndStandingForASystemThatNamesNoConnection_IsRefusedNamingTheSystem()
    {
        var telling = TestGraphs.TellingPipeline(namesItsConnection: false);

        var act = () => PipelineDagBuilder.Build(telling.Fixture.Build(), telling.PipelineId);

        act.Should().Throw<PipelineModelException>().WithMessage("*'Reporting office'*names no connection it is told through*");
    }

    [Fact] // A boundary pipeline validates: every wire connects real ports and the graph is acyclic.
    public void Validate_BoundaryPipeline_IsValid()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        DagValidator.Validate(dag).IsValid.Should().BeTrue();
    }

    [Fact] // A wire held as a Thing carries the same port mapping an edge-shaped wire carries.
    public void Build_WiresHeldAsThings_ResolveTheirPorts()
    {
        var (fx, pipelineId) = TestGraphs.ParallelWirePipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var generate = dag.Node(fx.Get("Generate").Id)!;
        var echo = dag.Node(fx.Get("Echo").Id)!;
        dag.Wires.Should().OnlyContain(w => w.FromNodeId == generate.NodeId && w.ToNodeId == echo.NodeId);
        dag.Wires.Select(w => (w.FromPort, w.ToPort)).Should()
            .BeEquivalentTo(new[] { ("echo", "message"), ("trace", "context") });
    }

    [Fact] // Two wires between one node pair — the case an edge-shaped wire cannot express at all.
    public void Build_TwoWireThingsBetweenOneNodePair_YieldsBoth()
    {
        var (fx, pipelineId) = TestGraphs.ParallelWirePipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.Wires.Should().HaveCount(2);
    }

    [Fact] // Ports are proved whichever shape the wire arrived in.
    public void Validate_ParallelWirePipeline_IsValid()
    {
        var (fx, pipelineId) = TestGraphs.ParallelWirePipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        DagValidator.Validate(dag).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Build_ResolvesNodesSubdomainsPortsAndWire()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.Nodes.Should().HaveCount(2);
        var generate = dag.Node(fx.Get("Generate").Id)!;
        var echo = dag.Node(fx.Get("Echo").Id)!;
        generate.Subdomain.Should().Be("gen");
        echo.Subdomain.Should().Be("ech");

        // ports resolve from the bound service's is-chain (proto)
        generate.Port("echo")!.IsOutput.Should().BeTrue();
        echo.Port("message")!.IsInput.Should().BeTrue();
        echo.Port("message")!.Required.Should().BeTrue();

        dag.Wires.Should().ContainSingle();
        var wire = dag.Wires[0];
        wire.FromNodeId.Should().Be(generate.NodeId);
        wire.ToNodeId.Should().Be(echo.NodeId);
        wire.FromPort.Should().Be("echo");
        wire.ToPort.Should().Be("message");
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
        fx.Rel(pipe, vocabulary.Has, first);
        fx.Rel(pipe, vocabulary.Has, second);
        fx.Rel(first, carries, second, ("fromPort", "produced"), ("toPort", "consumed"));

        var dag = PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        dag.Nodes.Should().HaveCount(2);
        dag.Node(first.Id)!.Subdomain.Should().Be("one");
        dag.Wires.Should().ContainSingle();
        DagValidator.Validate(dag).IsValid.Should().BeTrue();
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
            role.Id.Should().Be(StandardVersion5(PipelineRoles.RoleNamespace, role.Word), $"the role {role.Word}");
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

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>().WithMessage("*'Empty' has no nodes*");
    }

    // Without a subdomain there is no address to dispatch the node to, and a run would fail one node at a
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
        fx.Rel(pipe, vocabulary.Has, node);

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>().WithMessage("*'addressless' for node 'Echo' has no Subdomain*");
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

        var resolved = dag.Node(fx.Get("Echo").Id)!.Port("marked")!;
        resolved.Required.Should().Be(expected);
        resolved.Collection.Should().Be(expected);
    }

    [Fact]
    public void Build_ResolvesTheNodesOwnOutPortsBesideItsServices()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var decide = dag.Node(fx.Get("Decide").Id)!;
        decide.Ports.Select(p => p.PortName).Should().BeEquivalentTo("accept", "refuse", "in", "out");
        decide.Port("accept")!.IsOutput.Should().BeTrue();
        DagValidator.Validate(dag).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Build_ReadsWhetherAnOutPortProducesAThing()
    {
        var (fx, pipelineId) = TestGraphs.ProducingPipeline();

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var maker = dag.Node(fx.Get("Maker").Id)!;
        maker.Port("made")!.Produces.Should().BeTrue();
        maker.Port("note")!.Produces.Should().BeFalse();
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

        var unofferedBuild = () => PipelineDagBuilder.Build(unoffered.Fixture.Build(), unoffered.PipelineId);
        var twiceBuild = () => PipelineDagBuilder.Build(twice.Fixture.Build(), twice.PipelineId);

        unofferedBuild.Should().Throw<PipelineModelException>().WithMessage("*'Choose' chose 'At random', which no in port of it offers*");
        twiceBuild.Should().Throw<PipelineModelException>().WithMessage("*'Choose' chose two Things for its port 'strategy'*");
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

        var stateOnlyBuild = () => PipelineDagBuilder.Build(stateOnly.Fixture.Build(), stateOnly.PipelineId);
        var portOnlyBuild = () => PipelineDagBuilder.Build(portOnly.Fixture.Build(), portOnly.PipelineId);

        stateOnlyBuild.Should().Throw<PipelineModelException>()
            .WithMessage("*'Review' waits for 'decided' but names no in port whose Thing must enter it*");
        portOnlyBuild.Should().Throw<PipelineModelException>()
            .WithMessage("*'Review' waits through its port 'submission' but names no state for it to enter*");
    }

    [Fact]
    public void Build_ReadsThePipelineAnEndNodeStandsFor()
    {
        var starting = TestGraphs.StartingPipeline();

        var dag = PipelineDagBuilder.Build(starting.Fixture.Build(), starting.PipelineId);

        dag.Nodes.Single(node => node.Name == "Out").Starts.Should().Be(new Starting(starting.NextPipelineId, "What follows"));
        dag.Nodes.Single(node => node.Name == "Out").Tells.Should().BeNull();
    }

    [Fact]
    public void Build_RefusesAnEndNodeStandingForThePipelineItEnds()
    {
        var itself = TestGraphs.StartingPipeline(itself: true);

        var build = () => PipelineDagBuilder.Build(itself.Fixture.Build(), itself.PipelineId);

        build.Should().Throw<PipelineModelException>()
            .WithMessage("*'Out' stands for 'BoundaryDemo', the pipeline it ends, so a run would start another of itself without end*");
    }

    [Fact]
    public void Build_RefusesAWaitingNodeWaitingThroughTwoPorts()
    {
        var twoPorts = TestGraphs.WaitingPipeline();
        twoPorts.Fixture.Rel(twoPorts.Review, twoPorts.AwaitsThrough, twoPorts.Fixture.Get("p.notice"));

        var build = () => PipelineDagBuilder.Build(twoPorts.Fixture.Build(), twoPorts.PipelineId);

        build.Should().Throw<PipelineModelException>()
            .WithMessage("*'Review' waits through two ports, so no one Thing could be waited for*");
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

        var outPortBuild = () => PipelineDagBuilder.Build(outPort.Fixture.Build(), outPort.PipelineId);
        var foreignBuild = () => PipelineDagBuilder.Build(foreign.Fixture.Build(), foreign.PipelineId);

        outPortBuild.Should().Throw<PipelineModelException>().WithMessage("*'Review' waits through 'notice', which is not an in port of it*");
        foreignBuild.Should().Throw<PipelineModelException>().WithMessage("*'Review' waits through 'in', which is not an in port of it*");
    }

    [Fact]
    public void Build_RefusesANodeWaitingForTwoStates()
    {
        var waiting = TestGraphs.WaitingPipeline();
        waiting.Fixture.Rel(waiting.Review, waiting.ProvenBy, waiting.Fixture.Thing("withdrawn"));

        var build = () => PipelineDagBuilder.Build(waiting.Fixture.Build(), waiting.PipelineId);

        build.Should().Throw<PipelineModelException>().WithMessage("*'Review' waits for two states*");
    }

    [Fact]
    public void Validate_RefusesAWaitingNodeWhoseAwaitedPortNothingFills_AndOneRunningOncePerItem()
    {
        var unfilled = TestGraphs.WaitingPipeline(wired: false);
        var perItem = TestGraphs.WaitingPipeline(collection: true);

        var unfilledResult = DagValidator.Validate(PipelineDagBuilder.Build(unfilled.Fixture.Build(), unfilled.PipelineId));
        var perItemResult = DagValidator.Validate(PipelineDagBuilder.Build(perItem.Fixture.Build(), perItem.PipelineId));

        unfilledResult.Errors.Should().ContainSingle(error =>
            error == "'Review' waits for the Thing reaching its port 'submission', but nothing fills that port.");
        perItemResult.Errors.Should().ContainSingle(error =>
            error == "'Review' waits, but runs once for each item of 'submission'; a node that waits runs once.");
    }

    [Fact]
    public void Validate_AWaitingPipelineWhoseAwaitedPortIsWired_IsValid()
    {
        var waiting = TestGraphs.WaitingPipeline();

        DagValidator.Validate(PipelineDagBuilder.Build(waiting.Fixture.Build(), waiting.PipelineId)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Build_NodeWithoutConnection_Throws()
    {
        var fx = new GraphFixture();
        var vocabulary = fx.DeclareVocabulary();
        var pipe = fx.Thing("P");
        fx.Rel(pipe, vocabulary.Is, vocabulary.Pipeline);
        var node = fx.Thing("Lonely");
        fx.Rel(node, vocabulary.Is, vocabulary.PipelineNode);
        fx.Rel(pipe, vocabulary.Has, node); // node binds no connection

        var act = () => PipelineDagBuilder.Build(fx.Build(), pipe.Id);

        act.Should().Throw<PipelineModelException>().WithMessage("*binds no service connection*");
    }

    [Fact]
    public void Validate_DemoPipeline_IsAcyclicAndOrdered()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var result = DagValidator.Validate(dag);

        result.IsValid.Should().BeTrue();
        var order = result.Order.ToList();
        order.IndexOf(fx.Get("Generate").Id).Should().BeLessThan(order.IndexOf(fx.Get("Echo").Id));
    }

    [Fact]
    public void Validate_Cycle_IsRejected()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        // add the back-relationship Echo –carries(echo→message)→ Generate to make a cycle
        fx.Rel(fx.Get("Echo"), fx.Get("carries"), fx.Get("Generate"), ("fromPort", "echo"), ("toPort", "message"));
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var result = DagValidator.Validate(dag);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cycle"));
    }

    [Fact]
    public void Validate_WireToUnknownPort_IsRejected()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        // a wire whose target port does not exist on Echo
        fx.Rel(fx.Get("Generate"), fx.Get("carries"), fx.Get("Echo"), ("fromPort", "echo"), ("toPort", "nope"));
        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        var result = DagValidator.Validate(dag);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not an input port"));
    }
}
