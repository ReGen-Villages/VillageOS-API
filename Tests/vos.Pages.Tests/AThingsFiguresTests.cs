using System.Text.Json;
using Xunit;

namespace vos.Pages.Tests;

public class AThingsFiguresTests
{
    private const string English = "en";
    private static readonly CancellationToken s_none = CancellationToken.None;

    [Fact]
    public async Task The_inputs_of_a_figure_say_where_each_value_is_held_and_which_way_the_figure_moves()
    {
        using var reader = new PageReader();

        var read = Parsed(await reader.InputsOfAsync(HandBuiltModel.AsenReservoirId, HandBuiltModel.UsableCapacity, English, new ReadsAnsweredFromMemory(), s_none));

        var inputs = read.GetProperty("inputs").EnumerateArray().ToDictionary(input => input.GetProperty("term").GetString()!);
        var capacity = inputs[HandBuiltModel.Capacity];
        Assert.Equal("with", capacity.GetProperty("figureMoves").GetString());
        Assert.Equal("own", capacity.GetProperty("held").GetString());
        Assert.Equal(100, capacity.GetProperty("value").GetDouble());
        Assert.Equal(HandBuiltModel.AsenReservoirId, capacity.GetProperty("heldBy").GetString());

        var loss = inputs["lossFraction"];
        Assert.Equal("against", loss.GetProperty("figureMoves").GetString());
        Assert.Equal("inheritedOnly", loss.GetProperty("held").GetString());
        Assert.Equal(0.1, loss.GetProperty("inheritedValue").GetDouble());
        Assert.Equal("Reservoir", loss.GetProperty("inheritedFrom").GetString());
        Assert.Equal("assumed", loss.GetProperty("origin").GetString());
    }

    [Fact]
    public async Task With_no_figure_named_the_inputs_of_every_figure_the_Thing_derives_are_answered()
    {
        using var reader = new PageReader();

        var read = Parsed(await reader.InputsOfAsync(HandBuiltModel.AsenReservoirId, figure: null, English, new ReadsAnsweredFromMemory(), s_none));

        var figures = read.GetProperty("inputs").EnumerateArray().Select(input => input.GetProperty("figure").GetString()).Distinct();
        Assert.Equal([HandBuiltModel.UsableCapacity, HandBuiltModel.Inflow], figures);
    }

    [Fact]
    public async Task An_input_a_total_reads_names_the_kind_of_member_that_holds_it_and_that_kind_is_loaded_too()
    {
        using var reader = new PageReader();
        var reads = new ReadsAnsweredFromMemory();

        var read = Parsed(await reader.InputsOfAsync(HandBuiltModel.AsenReservoirId, HandBuiltModel.Inflow, English, reads, s_none));

        var flow = Assert.Single(read.GetProperty("inputs").EnumerateArray());
        Assert.Equal("Spring", flow.GetProperty("heldByEachMemberOfKind").GetString());
        Assert.False(flow.TryGetProperty("heldBy", out _));
        Assert.Equal(2, reads.Selectors.Count);
        Assert.Contains("Spring", JsonDocument.Parse(reads.Selectors.Last()).RootElement.GetProperty("names").EnumerateArray().Select(name => name.GetString()));
    }

    [Fact]
    public async Task A_Thing_none_of_whose_figures_totals_over_members_is_fetched_once_and_one_that_does_twice()
    {
        using var reader = new PageReader();
        var derivingNothing = new ReadsAnsweredFromMemory();
        var totallingOverSprings = new ReadsAnsweredFromMemory();

        var nothing = Parsed(await reader.InputsOfAsync(HandBuiltModel.ReservoirLevelsPageId, figure: null, English, derivingNothing, s_none));
        await reader.InputsOfAsync(HandBuiltModel.AsenReservoirId, HandBuiltModel.UsableCapacity, English, totallingOverSprings, s_none);

        Assert.Equal(0, nothing.GetProperty("inputs").GetArrayLength());
        Assert.Single(derivingNothing.Selectors);
        Assert.Equal(2, totallingOverSprings.Selectors.Count);
    }

    [Fact]
    public async Task A_Thing_whose_ranges_cannot_be_read_still_answers_its_inputs_with_no_way_out_of_a_state()
    {
        using var reader = new PageReader();
        var reads = new ReadsAnsweredFromMemory();

        var read = Parsed(await reader.InputsOfAsync(HandBuiltModel.AsenReservoirId, HandBuiltModel.UsableCapacity, English, reads, s_none));

        Assert.Equal($"/api/things/{HandBuiltModel.AsenReservoirId}/ranges", Assert.Single(reads.Got));
        Assert.All(read.GetProperty("inputs").EnumerateArray(), input => Assert.Equal(0, input.GetProperty("toLeave").GetArrayLength()));
    }

    [Fact]
    public async Task Where_a_value_came_from_is_answered_for_one_property()
    {
        using var reader = new PageReader();

        var own = Parsed(await reader.OriginOfAsync(HandBuiltModel.AsenReservoirId, HandBuiltModel.Capacity, new ReadsAnsweredFromMemory(), s_none));
        var inherited = Parsed(await reader.OriginOfAsync(HandBuiltModel.AsenReservoirId, "lossFraction", new ReadsAnsweredFromMemory(), s_none));
        var absent = Parsed(await reader.OriginOfAsync(HandBuiltModel.AsenReservoirId, "depthMetres", new ReadsAnsweredFromMemory(), s_none));

        Assert.Equal("own", own.GetProperty("held").GetString());
        Assert.Equal(("assumed", "Reservoir"), (inherited.GetProperty("origin").GetString(), inherited.GetProperty("assumedFrom").GetString()));
        Assert.Equal(("unknown", "notHeld"), (absent.GetProperty("origin").GetString(), absent.GetProperty("held").GetString()));
    }

    [Fact]
    public async Task A_figure_is_explained_by_what_it_was_worked_out_from_and_its_inputs()
    {
        using var reader = new PageReader();

        var read = Parsed(await reader.ExplainAsync(HandBuiltModel.AsenReservoirId, HandBuiltModel.UsableCapacity, English, new ReadsAnsweredFromMemory(), s_none));

        Assert.Equal(HandBuiltModel.UsableCapacity, read.GetProperty("figure").GetString());
        Assert.True(read.GetProperty("derived").GetBoolean());
        var worked = Assert.Single(read.GetProperty("workedOutFrom").EnumerateArray());
        Assert.Equal($"{HandBuiltModel.Capacity} * (1 - lossFraction)", worked.GetProperty("formula").GetString());
        Assert.Equal(["100", "0.1"], worked.GetProperty("inputs").EnumerateArray().Select(input => input.GetProperty("value").GetString()));
        Assert.Equal(2, read.GetProperty("inputs").GetArrayLength());
        Assert.Equal(0, read.GetProperty("judgedBy").GetArrayLength());
    }

    [Fact]
    public async Task A_Thing_the_model_does_not_hold_is_refused_by_its_identifier()
    {
        using var reader = new PageReader();
        const string unknown = "00000000-0000-4000-8000-00000000ffff";

        var refused = await Assert.ThrowsAnyAsync<Exception>(
            () => reader.InputsOfAsync(unknown, figure: null, English, new ReadsAnsweredFromMemory(), s_none));

        Assert.Contains(unknown, refused.Message);
    }

    private static JsonElement Parsed(string json) => JsonDocument.Parse(json).RootElement;
}
