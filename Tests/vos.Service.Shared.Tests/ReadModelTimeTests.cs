using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class ReadModelTimeTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly DateTimeOffset ModelInstant = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_broker_that_answers_gives_the_model_instant_and_its_rate()
    {
        var client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$$"""{"now":"{{{ModelInstant:o}}}","rate":60}""", Encoding.UTF8, "application/json"),
        });

        var reading = await client.ReadModelTimeAsync(CancellationToken.None);

        reading.Should().Be(new ModelTimeReading(ModelInstant, 60));
    }

    [Fact]
    public async Task A_read_the_client_gives_up_on_answers_null_like_any_other_failure()
    {
        var client = NewClient(_ => throw ElapsedClientTimeout.New());

        var reading = await client.ReadModelTimeAsync(CancellationToken.None);

        reading.Should().BeNull();
    }

    [Fact]
    public async Task A_read_the_caller_cancelled_throws_so_the_caller_can_stop()
    {
        using var caller = new CancellationTokenSource();
        var client = NewClient(_ => throw new OperationCanceledException(caller.Token));
        caller.Cancel();

        var read = () => client.ReadModelTimeAsync(caller.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Listening_yields_the_reading_taken_on_connecting_then_each_announced_change_and_skips_a_frame_it_cannot_read()
    {
        var announced = ModelInstant.AddHours(2);
        var frames = "event: ModelClockChanged\ndata: not json\n\n"
                     + "event: DaemonStatusChanged\ndata: {\"isRunning\":true}\n\n"
                     + $$$"""event: ModelClockChanged{{{"\n"}}}data: {"Now":"{{{announced:o}}}","Rate":300,"Simulated":true}{{{"\n\n"}}}""";
        var client = NewClient(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/time" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$$"""{"now":"{{{ModelInstant:o}}}","rate":60}""", Encoding.UTF8, "application/json"),
            },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(frames, Encoding.UTF8, "text/event-stream") },
        });

        var readings = await client.ListenForModelClockAsync(CancellationToken.None).ToListAsync();

        readings.Should().Equal(new ModelTimeReading(ModelInstant, 60), new ModelTimeReading(announced, 300));
    }

    [Fact]
    public async Task A_stream_the_broker_refuses_yields_nothing_so_the_caller_tries_again_later()
    {
        var client = NewClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var readings = await client.ListenForModelClockAsync(CancellationToken.None).ToListAsync();

        readings.Should().BeEmpty();
    }

    private static TestableMyceliumClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        // A client per call, as the host's factory gives: the stream is opened with no timeout and the clock
        // read with one, and one shared HttpClient refuses a change of timeout once it has sent a request.
        var factory = new PerCallHttpClientFactory(new MockHttpMessageHandler(respond));
        return new TestableMyceliumClient(factory, NullLogger.Instance, MyceliumUrl, launchToken: new LaunchToken("service-token"));
    }
}
