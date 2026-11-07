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

    private static TestableMyceliumClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new MockHttpMessageHandler(respond);
        var factory = new TestHttpClientFactory(new HttpClient(handler));
        return new TestableMyceliumClient(factory, NullLogger.Instance, MyceliumUrl, serviceToken: "service-token");
    }
}
