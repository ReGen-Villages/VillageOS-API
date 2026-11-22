using System.Net;
using System.Text;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class StudyPropertiesTests
{
    private const string Service = "EnergyBalance";
    private const string Mycelium = "http://mycelium.test";
    private static readonly Guid Study = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static (StudyProperties Properties, RecordingHttpMessageHandler Handler) Answering(
        HttpStatusCode status, string body = "")
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
        return (new StudyProperties(new HttpClient(handler), Mycelium, Service), handler);
    }

    [Fact]
    public async Task A_read_answers_the_study_s_properties_as_its_inputs()
    {
        var (properties, handler) = Answering(HttpStatusCode.OK, """{ "SiteStudy.population": { "Value": 300 } }""");

        var inputs = await properties.ReadAsync(Study);

        inputs.Number("population").Should().Be(300);
        handler.Requests.Should().ContainSingle()
            .Which.Should().Be(new RecordedRequest(HttpMethod.Get, $"{Mycelium}/api/things/{Study}/properties", ""));
    }

    [Fact]
    public async Task A_read_the_platform_refuses_names_the_service_the_study_and_the_status()
    {
        var (properties, _) = Answering(HttpStatusCode.NotFound);

        var read = () => properties.ReadAsync(Study);

        await read.Should().ThrowAsync<HttpRequestException>()
            .WithMessage($"{Service} could not read the study {Study} (404 NotFound)");
    }

    [Theory]
    [InlineData("null", "Null")]
    [InlineData("[]", "Array")]
    [InlineData("42", "Number")]
    public async Task An_answer_holding_no_properties_is_refused_as_a_broken_read(string body, string answered)
    {
        var (properties, _) = Answering(HttpStatusCode.OK, body);

        var read = () => properties.ReadAsync(Study);

        await read.Should().ThrowAsync<HttpRequestException>()
            .WithMessage($"{Service} read the study {Study} and was answered {answered} where its properties belong");
    }

    [Fact]
    public async Task A_write_posts_the_value_as_a_fact_on_the_property()
    {
        var (properties, handler) = Answering(HttpStatusCode.OK);

        await properties.WriteAsync(Study, "peopleFed", 12.5);

        handler.Requests.Should().ContainSingle().Which.Should().Be(new RecordedRequest(
            HttpMethod.Post, $"{Mycelium}/api/things/{Study}/properties/peopleFed/facts", """{"value":12.5}"""));
    }

    [Fact]
    public async Task A_write_the_platform_refuses_names_the_service_the_thing_and_the_property()
    {
        var (properties, _) = Answering(HttpStatusCode.Forbidden);

        var write = () => properties.WriteAsync(Study, "peopleFed", 12.5);

        await write.Should().ThrowAsync<HttpRequestException>()
            .WithMessage($"{Service} could not write {Study}.peopleFed (403 Forbidden)");
    }
}
