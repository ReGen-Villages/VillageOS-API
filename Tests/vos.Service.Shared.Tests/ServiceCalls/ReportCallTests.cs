using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.ServiceCalls;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests.ServiceCall;

public class ReportCallTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string ServiceToken = "service-token-abc";
    private static readonly Guid CallRecord = Guid.Parse("3f2a9c1e-7b40-4d6e-9a85-0c1d2e3f4a5b");

    public static TheoryData<string, string> EachReportAndItsBody() => new()
    {
        { "received", """{"report":"received"}""" },
        { "completed", """{"report":"completed"}""" },
        { "failed", """{"report":"failed","reason":"the rainfall source answered nothing"}""" },
    };

    private static CallReport Named(string word) => word switch
    {
        "received" => CallReport.Received,
        "completed" => CallReport.Completed,
        _ => CallReport.Failed("the rainfall source answered nothing"),
    };

    [Theory]
    [MemberData(nameof(EachReportAndItsBody))]
    public async Task Each_report_is_posted_to_the_call_records_report_route_with_its_word_and_any_reason(string word, string body)
    {
        var (client, handler) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await client.ReportCallAsync(CallRecord, Named(word));

        var sent = handler.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/relationships/{CallRecord}/report");
        sent.Headers.Authorization!.ToString().Should().Be($"Bearer {ServiceToken}");
        (await sent.Content!.ReadAsStringAsync()).Should().Be(body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_failed_report_without_a_reason_is_refused_before_anything_is_sent(string? reason)
    {
        var (client, handler) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var reporting = () => client.ReportCallAsync(CallRecord, CallReport.Failed(reason!));

        (await reporting.Should().ThrowAsync<ArgumentException>()).WithParameterName("reason");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_record_no_longer_waiting_is_raised_as_such_with_the_brokers_words()
    {
        var (client, _) = BuildClient(_ => Json(HttpStatusCode.Conflict, """{"error":"the call is already Completed"}"""));

        var reporting = () => client.ReportCallAsync(CallRecord, CallReport.Completed);

        var raised = (await reporting.Should().ThrowAsync<CallRecordNoLongerWaitingException>()).Which;
        raised.CallRecordId.Should().Be(CallRecord);
        raised.StatusCode.Should().Be(HttpStatusCode.Conflict);
        raised.Message.Should().Contain("the call is already Completed");
    }

    [Fact]
    public async Task Any_other_refusal_is_raised_with_the_brokers_status()
    {
        var (client, _) = BuildClient(_ => Json(HttpStatusCode.Forbidden, """{"error":"only the called service may report"}"""));

        var reporting = () => client.ReportCallAsync(CallRecord, CallReport.Received);

        var raised = (await reporting.Should().ThrowAsync<HttpRequestException>()).Which;
        raised.Should().NotBeOfType<CallRecordNoLongerWaitingException>();
        raised.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static (TestableMyceliumClient Client, MockHttpMessageHandler Handler) BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new MockHttpMessageHandler(respond);
        var client = new TestableMyceliumClient(new TestHttpClientFactory(new HttpClient(handler)), NullLogger.Instance, MyceliumUrl,
            new LaunchToken(ServiceToken));
        return (client, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
