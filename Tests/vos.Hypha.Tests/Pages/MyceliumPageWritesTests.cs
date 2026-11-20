using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Hypha.Registry;
using vos.Hypha.Tools.Pages;
using vos.Tests.Shared;
using Xunit;
using static vos.Hypha.Tests.ServerUnderTest;

namespace vos.Hypha.Tests.Pages;

// Each write is checked again as it is sent, because the writes the engine sends need not be the ones checked before.
public class MyceliumPageWritesTests
{
    private const string Row = "d5abcde5-800f-578f-8cb8-89b66825f468";
    private const string Predicate = "ae41e56e-ac72-5396-9c35-657dcb561b1c";

    [Fact]
    public void A_write_of_a_kind_a_press_never_makes_is_refused_by_its_kind() =>
        MyceliumPageWrites.RefusalToSend(new JsonArray(new JsonObject { ["write"] = "delete" })).Should().Contain("\"delete\"");

    [Fact]
    public void A_post_that_names_no_route_is_refused() =>
        MyceliumPageWrites.RefusalToSend(new JsonArray(new JsonObject { ["write"] = "post" })).Should().Contain("/api/endpoints/");

    [Fact]
    public void An_empty_entry_among_the_writes_is_refused() =>
        MyceliumPageWrites.RefusalToSend(new JsonArray((JsonNode?)null)).Should().NotBeNull();

    [Fact]
    public async Task A_post_to_a_route_a_press_may_not_reach_is_refused_as_it_is_sent_and_nothing_is_sent()
    {
        var (writes, mycelium) = WritesTo();

        var sending = () => writes.PressAsync("/api/model", "{}");

        await sending.Should().ThrowAsync<InvalidOperationException>().WithMessage("*/api/model*");
        mycelium.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_relate_to_a_Thing_that_is_not_an_identifier_is_refused_as_it_is_sent_and_nothing_is_sent()
    {
        var (writes, mycelium) = WritesTo();

        var sending = () => writes.RelateAsync(Row, Predicate, "../model");

        await sending.Should().ThrowAsync<InvalidOperationException>().WithMessage("*\"../model\"*");
        mycelium.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stamp_of_a_row_that_is_not_an_identifier_is_refused_as_it_is_sent_and_nothing_is_sent()
    {
        var (writes, mycelium) = WritesTo();

        var sending = () => writes.StampAsync("../model", "decidedAt", "2026-11-20T00:00:00Z");

        await sending.Should().ThrowAsync<InvalidOperationException>().WithMessage("*\"../model\"*");
        mycelium.Requests.Should().BeEmpty();
    }

    private static (MyceliumPageWrites Writes, MockHttpMessageHandler Mycelium) WritesTo()
    {
        var mycelium = new MockHttpMessageHandler(MyceliumFor(() => "editor", _ => Json("{}")));
        var client = ServerProgram.ClientFor(MyceliumUrl, ApiKey, mycelium);
        return (new MyceliumPageWrites(client, new ActivityReport(client, NullLogger<ActivityReport>.Instance), "The spring runs dry each August"), mycelium);
    }
}
