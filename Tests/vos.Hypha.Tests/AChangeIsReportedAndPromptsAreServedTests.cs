using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using vos.Hypha.Registry;
using vos.PlatformClient;
using vos.Tests.Shared;
using Xunit;
using static vos.Hypha.Tests.ServerUnderTest;

namespace vos.Hypha.Tests;

public class AChangeIsReportedAndPromptsAreServedTests
{
    private static ActivityReport AReportTo(MockHttpMessageHandler mycelium) =>
        new(ServerProgram.ClientFor(MyceliumUrl, ApiKey, new HttpClient(mycelium)), NullLogger<ActivityReport>.Instance);

    [Fact]
    public async Task A_change_is_reported_to_Mycelium_as_an_action_made_through_this_program()
    {
        var mycelium = new MockHttpMessageHandler(MyceliumFor(() => "editor", _ => Json("{}")));

        var kept = await AReportTo(mycelium).ReportAsync("Renamed Spring to Reservoir");

        kept.Should().BeTrue();
        var report = mycelium.Requests.Single(request => request.RequestUri!.AbsolutePath == "/api/operator-activity");
        report.Headers.GetValues(MyceliumClient.ProgramHeader).Should().ContainSingle(ServerProgram.Name);
        (await report.Content!.ReadAsStringAsync()).Should()
            .Contain("\"kind\":\"action\"").And.Contain("Renamed Spring to Reservoir").And.Contain("\"succeeded\":true");
    }

    [Fact]
    public async Task A_report_Mycelium_refuses_is_answered_as_not_kept_and_throws_nothing()
    {
        var mycelium = new MockHttpMessageHandler(
            MyceliumFor(() => "editor", _ => Json("""{"error":"Only a person's own credentials report activity"}""", HttpStatusCode.Forbidden)));

        (await AReportTo(mycelium).ReportAsync("Renamed Spring to Reservoir")).Should().BeFalse();
    }

    [Fact]
    public async Task A_prompt_is_not_listed_for_a_caller_its_source_does_not_offer_it_to()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "viewer"), configure: services => services.AddSingleton<IPromptSource, APromptForAnEditor>());

        (await server.Client.ListPromptsAsync()).Should().BeEmpty("the source offers its prompt to an editor only");
    }

    [Fact]
    public async Task A_prompt_is_answered_by_the_source_that_holds_it()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor"), configure: services => services.AddSingleton<IPromptSource, APromptForAnEditor>());

        (await server.Client.ListPromptsAsync()).Select(prompt => prompt.Name).Should().Equal("review_changes");

        var prompt = await server.Client.GetPromptAsync("review_changes");
        prompt.Messages.Should().ContainSingle()
            .Which.Content.Should().BeOfType<TextContentBlock>().Which.Text.Should().Be("Review what changed today.");
    }

    [Fact]
    public async Task A_prompt_no_source_holds_is_refused_by_name()
    {
        await using var server = await StartAsync(MyceliumFor(() => "editor"));

        var asking = () => server.Client.GetPromptAsync("no_such_prompt").AsTask();

        (await asking.Should().ThrowAsync<ModelContextProtocol.McpException>()).Which.Message.Should().Contain("no_such_prompt");
    }

    [Fact]
    public async Task A_prompt_its_source_does_not_hold_is_refused_by_name()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor"), configure: services => services.AddSingleton<IPromptSource, APromptForAnEditor>());

        var asking = () => server.Client.GetPromptAsync("no_such_prompt").AsTask();

        (await asking.Should().ThrowAsync<ModelContextProtocol.McpException>()).Which.Message.Should().Contain("no_such_prompt");
    }

    private sealed class APromptForAnEditor : IPromptSource
    {
        public Task<IReadOnlyList<Prompt>> ListAsync(Caller caller, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Prompt>>(
                caller.Role == "editor" ? [new Prompt { Name = "review_changes" }] : []);

        public Task<GetPromptResult?> GetAsync(Caller caller, GetPromptRequestParams asked, CancellationToken cancellationToken) =>
            Task.FromResult(asked.Name == "review_changes"
                ? new GetPromptResult
                {
                    Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "Review what changed today." } }],
                }
                : null);
    }
}
