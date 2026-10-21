using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Service.Phloem.Model;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// The /handle surface in vos.Service.Phloem/Program.cs driven end to end for a dispatched relationship:
// the broker posts one body for a `runs` write and for a state entry, and what the target starts is read
// from the model. The resolution rule is pinned in PipelineStartTests; these cover what the route does
// with the answer.
public class HandleEndpointTests
{
    private static readonly Guid Drawn = Guid.Parse("d0000000-0000-0000-0000-000000000001");
    private static readonly Guid Watching = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid Loose = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid Submission = Guid.Parse("50000000-0000-0000-0000-000000000042");

    [Fact]
    public async Task Handle_AStateEntry_StartsThePipelineDrawnFromItsConnection()
    {
        await using var factory = new PhloemWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/handle", StateEntry(target: Watching));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accepted").GetBoolean().Should().BeTrue();
        body.GetProperty("pipelineId").GetGuid().Should().Be(Drawn);

        // The run is recorded against the pipeline the connection was resolved to, after the request returned.
        await Settle.UntilAsync(() => factory.RunsRecordedFor(Drawn) == 1, "the run record reaches the broker");
    }

    [Fact]
    public async Task Handle_AConnectionNothingIsDrawnFrom_IsRefusedByName()
    {
        await using var factory = new PhloemWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/handle", StateEntry(target: Loose));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("looseConnection");
        factory.RunsRecordedFor(Drawn).Should().Be(0);
    }

    [Fact]
    public async Task Handle_ARunsRelationshipNamingThePipeline_StillStartsIt()
    {
        await using var factory = new PhloemWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/handle", StateEntry(target: Drawn));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pipelineId").GetGuid().Should().Be(Drawn);
    }

    [Fact]
    public async Task Handle_ARelationshipWrittenAlongAConnection_StartsThePipelineDrawnFromThatConnection()
    {
        await using var factory = new PhloemWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/handle", StateEntry(target: Joined, relationship: WrittenAlongWatching));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pipelineId").GetGuid().Should().Be(Drawn);
        await Settle.UntilAsync(() => factory.RunsRecordedFor(Drawn) == 1, "the run record reaches the broker");
    }

    [Fact]
    public async Task Handle_ARelationshipTheModelDoesNotHold_IsRefused()
    {
        await using var factory = new PhloemWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/handle", StateEntry(target: Joined));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.RunsRecordedFor(Drawn).Should().Be(0);
    }

    private static readonly Guid Joined = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid WrittenAlongWatching = Guid.Parse("a0000000-0000-0000-0000-000000000007");

    public enum Start { ByHandAnsweredAtOnce, ByHandAwaited, ByADispatch }

    [Theory]
    [InlineData(Start.ByHandAnsweredAtOnce)]
    [InlineData(Start.ByHandAwaited)]
    [InlineData(Start.ByADispatch)]
    public async Task Handle_RecordsTheBrokersRequestIdentifierOnTheRun_HoweverTheRunIsStarted(Start start)
    {
        await using var factory = new PhloemWebApplicationFactory();
        var client = factory.CreateClient();
        var requestId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/handle")
        {
            Content = start == Start.ByADispatch
                ? StateEntry(target: Watching)
                : new StringContent(
                    JsonSerializer.Serialize(new { pipelineId = Drawn, async = start == Start.ByHandAnsweredAtOnce }),
                    Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Vos-Request-Id", requestId.ToString());

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        await Settle.UntilAsync(() => factory.RunsRecordedFor(Drawn) == 1, "the run record reaches the broker");
        factory.RunRecordFor(Drawn).Should().Contain(requestId.ToString());
    }

    [Fact]
    public async Task Handle_AHeaderThatIsNoIdentifier_StillStartsTheRun_WhichRecordsNoRequest()
    {
        await using var factory = new PhloemWebApplicationFactory();
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/handle") { Content = StateEntry(target: Watching) };
        request.Headers.Add("Vos-Request-Id", "not an identifier");

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        await Settle.UntilAsync(() => factory.RunsRecordedFor(Drawn) == 1, "the run record reaches the broker");
        factory.RunRecordFor(Drawn).Should().NotContain("requestId");
    }

    private static StringContent StateEntry(Guid target, Guid? relationship = null) => new(JsonSerializer.Serialize(new
    {
        relationshipId = relationship ?? Guid.NewGuid(),
        subjectId = Submission,
        targetId = target,
        subjectName = "Submission 42",
        targetName = "whatever the model calls it",
        properties = new { },
    }), Encoding.UTF8, "application/json");

    // Settings arrive through UseSetting because the settings reader falls back to those when no
    // command-line flags are present, and IHttpClientFactory is replaced so every call to the broker
    // reaches the stand-in below. The model holds one drawing: a start node standing for `watching`, held
    // by the pipeline `Drawn`, beside a connection nothing is drawn from.
    private sealed class PhloemWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly MockHttpMessageHandler _broker;

        public PhloemWebApplicationFactory() => _broker = new MockHttpMessageHandler(Respond);

        public int RunsRecordedFor(Guid pipelineId) => _broker.Requests.Count(request =>
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/things"
            && request.Content!.ReadAsStringAsync().Result.Contains(pipelineId.ToString()));

        public string RunRecordFor(Guid pipelineId) => _broker.Requests
            .Where(request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/things")
            .Select(request => request.Content!.ReadAsStringAsync().Result)
            .Single(body => body.Contains(pipelineId.ToString()));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Port", "5000");
            builder.UseSetting("MyceliumUrl", "http://localhost");
            builder.UseSetting("Token", "test-token");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(_broker));
            });
        }

        private static HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/api/subscriptions" && request.Method == HttpMethod.Post)
                return Json(OneDrawing());
            if (uri.AbsolutePath == "/api/things" && uri.Query.Contains("name="))
                return Json(JsonSerializer.Serialize(new { Id = Guid.NewGuid(), Name = "a predicate", Properties = new { } }));
            if (uri.AbsolutePath.StartsWith("/api/relationships/", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return uri.AbsolutePath.EndsWith(WrittenAlongWatching.ToString(), StringComparison.Ordinal)
                    ? Json(JsonSerializer.Serialize(new { Id = WrittenAlongWatching, SubjectId = Submission, PredicateId = Watching, TargetId = Joined, Properties = new { } }))
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private static string OneDrawing()
        {
            var pipelineArchetype = Guid.NewGuid();
            var startArchetype = Guid.NewGuid();
            var connectionArchetype = Guid.NewGuid();
            var isPredicate = Guid.NewGuid();
            var hasPredicate = Guid.NewGuid();
            var standsFor = Guid.NewGuid();
            var arrival = Guid.NewGuid();

            object Thing(Guid id, string name, string? flag = null) => new
            {
                id, name,
                properties = flag is null
                    ? new Dictionary<string, object>()
                    : new Dictionary<string, object> { [flag] = new { value = true, typeInfo = "vos.Boolean" } },
            };
            object Edge(Guid subject, Guid predicate, Guid target) =>
                new { id = Guid.NewGuid(), subjectId = subject, predicateId = predicate, targetId = target };

            return JsonSerializer.Serialize(new
            {
                snapshot = new
                {
                    things = new[]
                    {
                        Thing(pipelineArchetype, "Workflow", PipelineArchetypes.PipelineFlag),
                        Thing(startArchetype, "Doorway", PipelineArchetypes.PipelineInputFlag),
                        Thing(connectionArchetype, "Binding", PipelineArchetypes.ConnectionFlag),
                        Thing(isPredicate, "is"),
                        Thing(hasPredicate, "has"),
                        Thing(standsFor, "standsFor", PipelinePredicates.StandsForFlag),
                        Thing(Watching, "watchingConnection"),
                        Thing(Loose, "looseConnection"),
                        Thing(arrival, "Arrival"),
                        Thing(Drawn, "Drawn from the state"),
                    },
                    relationships = new[]
                    {
                        Edge(Watching, isPredicate, connectionArchetype),
                        Edge(Loose, isPredicate, connectionArchetype),
                        Edge(arrival, isPredicate, startArchetype),
                        Edge(arrival, standsFor, Watching),
                        Edge(Drawn, hasPredicate, arrival),
                        Edge(Drawn, isPredicate, pipelineArchetype),
                    },
                },
            });
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
