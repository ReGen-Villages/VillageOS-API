using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JsonataTransform = vos.Service.Shared.JsonataTransform;
using Microsoft.Extensions.Logging;
using NSubstitute;
using vos.Service.Shared;
using vos.Service.Tributary.Services;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// A call that names its subject reports the values it wrote (#6809). The discovery service resolves a
// fetched word into the edge the model declares, and the fetch response is the one place it can learn
// the word from: an observation is accepted into a queue and applied later, so reading the model back
// straight after the call races the drainer. Tributary already knows what it submitted — this only
// hands it back. The bulk path, readings naming their own entities, keeps its counts-only summary: a
// fetched page of readings echoed back would put the whole page in every response.
public class WrittenValuesTests
{
    private static ObservationIngestService Ingest(out IEndpointMyceliumClient client)
    {
        client = Substitute.For<IEndpointMyceliumClient>();
        return new ObservationIngestService(client, Substitute.For<ILogger<ObservationIngestService>>());
    }

    private static void SubjectPathAnswers(IEndpointMyceliumClient client, Guid subjectId)
    {
        var observedId = Guid.NewGuid();
        client.FindThingByNameAsync("observed").Returns(new MyceliumClient.MyceliumThing(observedId, "observed"));
        client.CreateRelationshipAsync(Arg.Any<Guid>(), observedId, subjectId).Returns(true);
        client.SubmitObservationsAsync(subjectId, Arg.Any<IReadOnlyList<ObservationSample>>()).Returns(true);
    }

    [Fact]
    public async Task A_subject_call_reports_the_values_it_wrote()
    {
        var subjectId = Guid.NewGuid();
        var sut = Ingest(out var client);
        SubjectPathAnswers(client, subjectId);

        var result = await sut.CreateObservationsAsync(
            Guid.NewGuid(),
            new JsonataTransform("{\"properties\":{\"flowRegime\":\"steady\",\"depthMetres\":2}}"),
            "{}", subjectId);

        result.Success.Should().BeTrue(result.Error);
        result.Written.Should().NotBeNull();
        // The values ride as the reshape's own JSON elements; the response serializes them verbatim.
        result.Written!["flowRegime"]!.ToString().Should().Be("steady");
        result.Written!.Should().ContainKey("depthMetres");
    }

    // A reshape that declined to claim anything — a provider hedge, a coordinate it cannot classify —
    // reports an empty set rather than nothing at all: the call succeeded and wrote no value, which a
    // reader must be able to tell from a call whose path never reports writes.
    [Fact]
    public async Task A_subject_call_that_wrote_nothing_reports_an_empty_set()
    {
        var subjectId = Guid.NewGuid();
        var sut = Ingest(out var client);
        SubjectPathAnswers(client, subjectId);

        var result = await sut.CreateObservationsAsync(
            Guid.NewGuid(), new JsonataTransform("{\"properties\":{}}"), "{}", subjectId);

        result.Success.Should().BeTrue(result.Error);
        result.Written.Should().NotBeNull();
        result.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task The_bulk_reading_path_keeps_its_counts_only_summary()
    {
        var entityId = Guid.NewGuid();
        var sut = Ingest(out var client);
        client.FindThingByNameAsync("Willow Bend spring")
            .Returns(new MyceliumClient.MyceliumThing(entityId, "Willow Bend spring"));
        var observedId = Guid.NewGuid();
        client.FindThingByNameAsync("observed").Returns(new MyceliumClient.MyceliumThing(observedId, "observed"));
        client.CreateRelationshipAsync(Arg.Any<Guid>(), observedId, entityId).Returns(true);
        client.SubmitObservationsAsync(entityId, Arg.Any<IReadOnlyList<ObservationSample>>()).Returns(true);

        var result = await sut.CreateObservationsAsync(
            Guid.NewGuid(),
            new JsonataTransform("{\"name\":\"Willow Bend spring\",\"properties\":{\"flowRegime\":\"steady\"}}"),
            "{}");

        result.Success.Should().BeTrue(result.Error);
        result.Written.Should().BeNull();
    }

    [Fact]
    public async Task The_handle_response_carries_what_a_subject_call_wrote()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var observedId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":               {"Value":"https://api.test/point"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"{\"properties\": {\"flowRegime\": regime}}"}
        }
        """;
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.Method == HttpMethod.Post
                && req.RequestUri!.AbsolutePath == $"/api/things/{subjectId}/observations")
                return Json("""{"accepted":1}""");
            if (req.RequestUri!.Host == "api.test")
                return Json("""{"regime":"steady"}""");
            return RouteFindThing(req, thingId, "EP")
                ?? RouteFindThing(req, observedId, "observed")
                ?? RouteEffectiveProperties(req, thingId, props)
                ?? RouteKindsFromProperties(req, thingId, props)
                ?? RouteRelationshipWrite(req)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { endpointName = "EP", subjectId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"written\":{\"flowRegime\":\"steady\"}");
    }
}
