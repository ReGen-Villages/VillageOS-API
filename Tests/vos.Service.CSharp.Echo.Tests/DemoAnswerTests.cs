using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.CSharp.Echo.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.CSharp.Echo.Tests;

public class DemoAnswerTests
{
    private const string MyceliumUrl = "http://localhost:7243";

    [Fact]
    public async Task A_demo_run_holding_no_credential_answers_why_and_calls_nothing()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new EndpointServiceMyceliumClient(new PerCallHttpClientFactory(handler),
            NullLogger<EndpointServiceMyceliumClient>.Instance, "Echo", MyceliumUrl, serviceToken: null);

        var answer = await DemoAnswer.OfAsync(() => new WriteKindsDemo(client).RunAsync(Guid.NewGuid(), DateTime.UtcNow));

        ((IStatusCodeHttpResult)answer).StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        ReasonIn(answer).Should().Contain("neither ApiKey nor Token is set");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_write_the_platform_refuses_answers_with_the_refusal()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        var client = new EndpointServiceMyceliumClient(new PerCallHttpClientFactory(handler),
            NullLogger<EndpointServiceMyceliumClient>.Instance, "Echo", MyceliumUrl, serviceToken: "svc-jwt-abc");

        var answer = await DemoAnswer.OfAsync(() => new WriteKindsDemo(client).RunAsync(Guid.NewGuid(), DateTime.UtcNow));

        ((IStatusCodeHttpResult)answer).StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        ReasonIn(answer).Should().Contain("405");
    }

    [Fact]
    public async Task A_demo_that_runs_answers_its_result()
    {
        var answer = await DemoAnswer.OfAsync(() => Task.FromResult(42));

        answer.Should().BeOfType<Ok<int>>().Which.Value.Should().Be(42);
    }

    private static string ReasonIn(IResult answer) =>
        ((IValueHttpResult)answer).Value!.GetType().GetProperty("error")!.GetValue(((IValueHttpResult)answer).Value)!.ToString()!;
}
