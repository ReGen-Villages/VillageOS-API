using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

// One registration serving many subjects, all the way to where the reading lands. The address half
// is pinned in AddressParameterHandleTests; this is the other half — which Thing the observation is
// written onto when the same registration is called for two different sites (Bug #6532).
public class PerSiteIngestTests
{
    // The expression names a site, as the documented examples do. That name is what every call would
    // otherwise write onto, whichever site the call was actually about.
    private const string SiteEndpointProperties = """
    {
      "Endpoint.url":               {"Value":"https://api.test/point?lat={lat}&lon={lon}"},
      "Endpoint.httpMethod":        {"Value":"GET"},
      "Endpoint.responseTransform": {"Value":"{\"name\": \"WillowBend\", \"properties\": {\"precipitation\": mm}}"}
    }
    """;

    private const string UnnamedReadingProperties = """
    {
      "Endpoint.url":               {"Value":"https://api.test/point?lat={lat}&lon={lon}"},
      "Endpoint.httpMethod":        {"Value":"GET"},
      "Endpoint.responseTransform": {"Value":"{\"properties\": {\"precipitation\": mm}}"}
    }
    """;

    private sealed class Recorder
    {
        public List<Guid> ObservedOn { get; } = new();
        public List<string> ThingsCreated { get; } = new();
    }

    private static TributaryWebApplicationFactory FactoryOver(
        Recorder recorder, Guid endpointId, string properties)
    {
        var factory = new TributaryWebApplicationFactory();
        factory.HandlerCallback = req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path.EndsWith("/observations"))
            {
                recorder.ObservedOn.Add(Guid.Parse(path.Split('/')[3]));
                return Json("""{"accepted":1}""");
            }
            if (req.Method == HttpMethod.Post && path == "/api/things")
            {
                recorder.ThingsCreated.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return Json("{\"Id\":\"" + Guid.NewGuid() + "\",\"Name\":\"x\",\"Properties\":{}}");
            }
            if (req.RequestUri.Host == "api.test") return Json("""{"mm":3.4}""");
            return RouteFindThing(req, endpointId, "EP")
                ?? RouteEffectiveProperties(req, endpointId, properties)
                ?? RouteKindsFromProperties(req, endpointId, properties)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        return factory;
    }

    private static object CallFor(Guid subjectId, string lat, string lon) => new
    {
        endpointName = "EP",
        subjectId,
        addressParameters = new Dictionary<string, string> { ["lat"] = lat, ["lon"] = lon }
    };

    [Fact]
    public async Task Handle_OneRegistrationCalledForTwoSubjects_ObservesOntoEachRespectiveSubject()
    {
        var recorder = new Recorder();
        var willowBend = Guid.NewGuid();
        var eastfield = Guid.NewGuid();
        await using var factory = FactoryOver(recorder, Guid.NewGuid(), SiteEndpointProperties);
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        foreach (var (subject, lat, lon) in new[] { (willowBend, "39.4", "-8.2"), (eastfield, "41.1", "-7.6") })
            (await client.PostAsJsonAsync("/handle", CallFor(subject, lat, lon)))
                .StatusCode.Should().Be(HttpStatusCode.OK);

        recorder.ObservedOn.Should().Equal(willowBend, eastfield);
    }

    [Fact]
    public async Task Handle_SubjectSupplied_CreatesNoThingForTheNameTheExpressionCarries()
    {
        // The expression names WillowBend on every call. Resolving that name is what would mint a
        // Thing per name the registration happens to carry, and relate the endpoint to the wrong one.
        var recorder = new Recorder();
        await using var factory = FactoryOver(recorder, Guid.NewGuid(), SiteEndpointProperties);
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/handle", CallFor(Guid.NewGuid(), "39.4", "-8.2"));

        recorder.ThingsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SubjectSupplied_ReadingNeedNotNameAnything()
    {
        // A call that says what it is about has already answered the question the name answers, so a
        // registration serving many subjects can stop carrying a name that fits only one of them.
        var recorder = new Recorder();
        var subject = Guid.NewGuid();
        await using var factory = FactoryOver(recorder, Guid.NewGuid(), UnnamedReadingProperties);
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", CallFor(subject, "39.4", "-8.2"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"observationsSubmitted\":1");
        recorder.ObservedOn.Should().Equal(subject);
    }

    [Fact]
    public async Task Handle_SubjectSuppliedAndTheReadingCarriesNoValues_SubmitsNothingAndSucceeds()
    {
        // A source can answer for a site it holds nothing about. There is no observation to write and
        // no failure to report — reporting one would put a fabricated outage in front of a planner.
        var recorder = new Recorder();
        const string noValues = """
        {
          "Endpoint.url":               {"Value":"https://api.test/point?lat={lat}&lon={lon}"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"{\"properties\": {}}"}
        }
        """;
        await using var factory = FactoryOver(recorder, Guid.NewGuid(), noValues);
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", CallFor(Guid.NewGuid(), "39.4", "-8.2"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"observationsSubmitted\":0");
        recorder.ObservedOn.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SubjectSuppliedAndTheObservationIsRefused_ReportsTheFailure()
    {
        var endpointId = Guid.NewGuid();
        var factory = new TributaryWebApplicationFactory();
        factory.HandlerCallback = req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path.EndsWith("/observations"))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (req.RequestUri.Host == "api.test") return Json("""{"mm":3.4}""");
            return RouteFindThing(req, endpointId, "EP")
                ?? RouteEffectiveProperties(req, endpointId, SiteEndpointProperties)
                ?? RouteKindsFromProperties(req, endpointId, SiteEndpointProperties)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        await using var _ = factory;
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", CallFor(Guid.NewGuid(), "39.4", "-8.2"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Failed to submit observations");
    }

    [Fact]
    public async Task Handle_NoSubjectAndNoNameInTheReading_IsRefused()
    {
        // Neither half says where the reading belongs, so there is nothing to write it onto but a
        // guess. Refusing is the only answer that cannot attribute a value to the wrong Thing.
        var recorder = new Recorder();
        await using var factory = FactoryOver(recorder, Guid.NewGuid(), UnnamedReadingProperties);
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["lat"] = "39.4", ["lon"] = "-8.2" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("'name'");
        recorder.ObservedOn.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoSubject_StillResolvesTheReadingByName()
    {
        // The name path is what every existing registration uses, and a subject is only supplied by a
        // caller serving many. Nothing about this changes for a registration that serves one.
        var recorder = new Recorder();
        var endpointId = Guid.NewGuid();
        var willowBend = Guid.NewGuid();
        var factory = new TributaryWebApplicationFactory();
        factory.HandlerCallback = req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path.EndsWith("/observations"))
            {
                recorder.ObservedOn.Add(Guid.Parse(path.Split('/')[3]));
                return Json("""{"accepted":1}""");
            }
            if (req.RequestUri.Host == "api.test") return Json("""{"mm":3.4}""");
            return RouteFindThing(req, endpointId, "EP")
                ?? RouteFindThing(req, willowBend, "WillowBend")
                ?? RouteEffectiveProperties(req, endpointId, SiteEndpointProperties)
                ?? RouteKindsFromProperties(req, endpointId, SiteEndpointProperties)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        await using var _ = factory;
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            endpointName = "EP",
            addressParameters = new Dictionary<string, string> { ["lat"] = "39.4", ["lon"] = "-8.2" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        recorder.ObservedOn.Should().Equal(willowBend);
    }
}
