using System.Net;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using vos.Mcp.Registry;
using Xunit;
using static vos.Mcp.Tests.ServerUnderTest;

namespace vos.Mcp.Tests;

public class AChangeIsConfirmedBeforeItIsWrittenTests
{
    private const string AnswersACallWithInputRequired = "2026-07-28";
    private const string SendsTheOlderRequest = "2025-11-25";

    private static readonly Guid Thing = Guid.Parse("0d6c1a0e-9f55-4a0e-8a59-0d3e7b0c2f44");

    private string _name = "Spring";

    private HttpResponseMessage AModelHoldingTheThing(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
            ? Json($$"""{"Id":"{{Thing}}","Name":"{{_name}}"}""")
            : Json("{}");

    private static Dictionary<string, object?> TheThing(string? digest = null) =>
        digest is null
            ? new() { ["thing"] = Thing }
            : new() { ["thing"] = Thing, [Confirmation.DigestParameter] = digest };

    private readonly List<string> _shownToThePerson = [];

    private McpClientOptions AChatApplicationThatAsksThePerson(string protocolVersion, string answer) => new()
    {
        ProtocolVersion = protocolVersion,
        Handlers = new McpClientHandlers
        {
            ElicitationHandler = (asked, _) =>
            {
                _shownToThePerson.Add(asked!.Message);
                return ValueTask.FromResult(new ElicitResult { Action = answer });
            },
        },
    };

    private static int Deletions(ServerUnderTest server) =>
        server.Mycelium.Requests.Count(request => request.Method == HttpMethod.Delete);

    [Theory]
    [InlineData(AnswersACallWithInputRequired)]
    [InlineData(SendsTheOlderRequest)]
    public async Task The_person_is_shown_the_change_and_a_yes_writes_it(string protocolVersion)
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", AModelHoldingTheThing),
            AChatApplicationThatAsksThePerson(protocolVersion, "accept"), toolTypes: typeof(SampleTools));

        var answer = await server.Client.CallToolAsync("sample_delete", TheThing());

        TextOf(answer).Should().Be("deleted");
        _shownToThePerson.Should().Equal("Delete the Thing named Spring.");
        Deletions(server).Should().Be(1);
    }

    [Theory]
    [InlineData(AnswersACallWithInputRequired)]
    [InlineData(SendsTheOlderRequest)]
    public async Task A_no_writes_nothing(string protocolVersion)
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", AModelHoldingTheThing),
            AChatApplicationThatAsksThePerson(protocolVersion, "decline"), toolTypes: typeof(SampleTools));

        var answer = await server.Client.CallToolAsync("sample_delete", TheThing());

        TextOf(answer).Should().Contain("declined");
        Deletions(server).Should().Be(0);
    }

    [Fact]
    public async Task A_newer_chat_application_is_asked_in_the_answer_to_its_call()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", AModelHoldingTheThing),
            AChatApplicationThatAsksThePerson(AnswersACallWithInputRequired, "accept"), toolTypes: typeof(SampleTools));

        await server.Client.CallToolAsync("sample_delete", TheThing());

        server.SentToTheChatApplication.Should().Contain("inputRequests")
            .And.Contain(Confirmation.DigestOf("Delete the Thing named Spring."))
            .And.NotContain("\"method\":\"elicitation/create\",\"id\"");
    }

    [Fact]
    public async Task A_change_that_is_no_longer_what_the_person_was_shown_is_shown_again_before_it_is_written()
    {
        var chatApplication = AChatApplicationThatAsksThePerson(AnswersACallWithInputRequired, "accept");
        var asking = chatApplication.Handlers.ElicitationHandler!;
        chatApplication.Handlers.ElicitationHandler = (asked, cancellationToken) =>
        {
            if (_shownToThePerson.Count == 0) _name = "Reservoir";
            return asking(asked, cancellationToken);
        };
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", AModelHoldingTheThing), chatApplication, toolTypes: typeof(SampleTools));

        var answer = await server.Client.CallToolAsync("sample_delete", TheThing());

        _shownToThePerson.Should().Equal("Delete the Thing named Spring.", "Delete the Thing named Reservoir.");
        TextOf(answer).Should().Be("deleted");
        Deletions(server).Should().Be(1);
    }

    [Fact]
    public async Task An_older_chat_application_is_sent_the_older_request()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", AModelHoldingTheThing),
            AChatApplicationThatAsksThePerson(SendsTheOlderRequest, "accept"), toolTypes: typeof(SampleTools));

        await server.Client.CallToolAsync("sample_delete", TheThing());

        server.SentToTheChatApplication.Should().Contain("elicitation/create").And.NotContain("inputRequests");
    }

    [Fact]
    public async Task Where_the_person_cannot_be_asked_a_change_that_destroys_nothing_is_written_at_once()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", AModelHoldingTheThing), toolTypes: typeof(SampleTools));

        var answer = await server.Client.CallToolAsync(
            "sample_set", new Dictionary<string, object?> { ["thing"] = Thing, ["value"] = "high" });

        TextOf(answer).Should().Be("set");
    }

    [Fact]
    public async Task Where_the_person_cannot_be_asked_a_change_that_destroys_is_shown_first_and_written_on_its_digest()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", AModelHoldingTheThing), toolTypes: typeof(SampleTools));

        var shown = await server.Client.CallToolAsync("sample_delete", TheThing());

        var digest = Confirmation.DigestOf("Delete the Thing named Spring.");
        TextOf(shown).Should().Contain("Delete the Thing named Spring.").And.Contain(digest);
        Deletions(server).Should().Be(0);

        var written = await server.Client.CallToolAsync("sample_delete", TheThing(digest));

        TextOf(written).Should().Be("deleted");
        Deletions(server).Should().Be(1);
    }

    [Fact]
    public async Task A_digest_of_what_the_model_no_longer_holds_writes_nothing_and_shows_the_change_as_it_is_now()
    {
        await using var server = await StartAsync(
            MyceliumFor(() => "editor", AModelHoldingTheThing), toolTypes: typeof(SampleTools));
        var stale = Confirmation.DigestOf("Delete the Thing named Spring.");
        _name = "Reservoir";

        var shown = await server.Client.CallToolAsync("sample_delete", TheThing(stale));

        TextOf(shown).Should().Contain("Delete the Thing named Reservoir.")
            .And.Contain(Confirmation.DigestOf("Delete the Thing named Reservoir."));
        Deletions(server).Should().Be(0);
    }
}
