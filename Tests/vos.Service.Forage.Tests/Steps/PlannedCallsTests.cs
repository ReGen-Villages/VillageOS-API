using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Forage.Steps;
using Xunit;

namespace vos.Service.Forage.Tests.Steps;

public class PlannedCallsTests
{
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid Source = Guid.NewGuid();
    private static readonly Guid Record = Guid.NewGuid();

    private static OutstandingCall Outstanding(IReadOnlyDictionary<string, string> values) => new(
        new CoveringSource(Source, "OpenMeteo", "OpenMeteoEndpoint", []),
        new SourceCall(Site, "WillowBend", values),
        Record, Attempts: 2);

    private static Dictionary<string, string> Window(string from, string to) => new()
    {
        [CoveringSourceResolver.WindowStartPlaceholder] = from,
        [CoveringSourceResolver.WindowEndPlaceholder] = to,
        ["lat"] = "39.4",
    };

    [Fact]
    public void A_call_with_no_long_history_is_one_item_carrying_its_record()
    {
        var planned = PlannedCalls.From([Outstanding(new Dictionary<string, string> { ["lat"] = "39.4" })], daysPerSlice: 365)
            .Should().ContainSingle().Subject;

        planned.Should().Be(new PlannedCall("OpenMeteoEndpoint", Site, planned.AddressParameters, Source, "OpenMeteo", "WillowBend", Record, 2));
        planned.AddressParameters["lat"].Should().Be("39.4");
    }

    [Fact]
    public void A_long_history_is_one_item_per_slice_each_asking_for_its_own_dates()
    {
        var planned = PlannedCalls.From([Outstanding(Window("2020-01-01", "2022-12-31"))], daysPerSlice: 365);

        planned.Select(call => (call.AddressParameters[CoveringSourceResolver.WindowStartPlaceholder], call.AddressParameters[CoveringSourceResolver.WindowEndPlaceholder]))
            .Should().Equal(("2020-01-01", "2020-12-30"), ("2020-12-31", "2021-12-30"), ("2021-12-31", "2022-12-30"), ("2022-12-31", "2022-12-31"));
        planned.Select(call => call.Slice).Should().Equal(new CallSlice(0, 4), new CallSlice(1, 4), new CallSlice(2, 4), new CallSlice(3, 4));
        planned.Should().AllSatisfy(call => call.AddressParameters["lat"].Should().Be("39.4"));
    }

    private static Dictionary<string, string> WindowStating(string daysPerSlice) =>
        new(Window("1991-01-01", "2025-12-31")) { [PlannedCalls.DaysPerSliceProperty] = daysPerSlice };

    [Fact]
    public void A_source_stating_a_slice_as_long_as_its_window_is_one_call()
    {
        var planned = PlannedCalls.From([Outstanding(WindowStating("13000"))], daysPerSlice: 365);

        var call = planned.Should().ContainSingle().Subject;
        call.Slice.Should().BeNull();
        call.AddressParameters[CoveringSourceResolver.WindowStartPlaceholder].Should().Be("1991-01-01");
        call.AddressParameters[CoveringSourceResolver.WindowEndPlaceholder].Should().Be("2025-12-31");
    }

    [Fact]
    public void A_source_stating_its_slice_length_is_cut_into_slices_of_that_many_days()
    {
        var planned = PlannedCalls.From([Outstanding(WindowStating("5000"))], daysPerSlice: 365);

        planned.Select(call => call.AddressParameters[CoveringSourceResolver.WindowStartPlaceholder])
            .Should().Equal("1991-01-01", "2004-09-09", "2018-05-19");
    }

    [Fact]
    public void A_source_stating_no_slice_length_takes_the_services_own()
    {
        var planned = PlannedCalls.From([Outstanding(Window("1991-01-01", "2025-12-31"))], daysPerSlice: 365);

        planned.Should().HaveCount(36);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("1.5")]
    [InlineData("a year")]
    [InlineData("")]
    public void A_slice_length_that_is_not_a_whole_number_of_days_refuses_the_plan_naming_the_source(string stated)
    {
        var plan = () => PlannedCalls.From([Outstanding(WindowStating(stated))], daysPerSlice: 365);

        plan.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("'OpenMeteo'").And.Contain(PlannedCalls.DaysPerSliceProperty);
    }

    private static ItemOutcome Slice(PlannedCall call, string? reason, string? word = null) =>
        new(call, word is null ? null : new Dictionary<string, string> { ["level" + call.Slice!.Index] = word }, reason);

    [Fact]
    public void A_call_resolves_only_when_every_slice_did_with_what_each_wrote_together()
    {
        var slices = PlannedCalls.From([Outstanding(Window("2020-01-01", "2021-12-31"))], daysPerSlice: 365);

        var (call, result) = PlannedCalls.Fold(slices.Select((slice, index) => Slice(slice, null, $"word{index}"))).Should().ContainSingle().Subject;

        result.Resolved.Should().BeTrue();
        result.Written.Should().BeEquivalentTo(new Dictionary<string, string> { ["level0"] = "word0", ["level1"] = "word1", ["level2"] = "word2" });
        call.RecordId.Should().Be(Record);
    }

    [Fact]
    public void A_call_with_a_failed_slice_keeps_that_slices_reason_and_dates()
    {
        var slices = PlannedCalls.From([Outstanding(Window("2020-01-01", "2021-12-31"))], daysPerSlice: 365);

        var (_, result) = PlannedCalls.Fold([Slice(slices[0], null, "low"), Slice(slices[1], "503: down"), Slice(slices[2], null, "high")])
            .Should().ContainSingle().Subject;

        result.Resolved.Should().BeFalse();
        result.Reason.Should().Be("2020-12-31 to 2021-12-30: 503: down");
        result.Written.Should().BeNull();
    }

    [Fact]
    public void A_call_missing_a_slice_is_unresolved_saying_how_many_came_back()
    {
        var slices = PlannedCalls.From([Outstanding(Window("2020-01-01", "2021-12-31"))], daysPerSlice: 365);

        var (_, result) = PlannedCalls.Fold([Slice(slices[0], null, "low")]).Should().ContainSingle().Subject;

        result.Reason.Should().Be("2 of 3 slices came back with no answer.");
    }

    [Fact]
    public void Calls_about_different_subjects_or_sources_fold_apart()
    {
        var first = PlannedCalls.From([Outstanding(new Dictionary<string, string>())], 365).Single();
        var second = first with { SourceId = Guid.NewGuid() };

        PlannedCalls.Fold([new ItemOutcome(first, null, null), new ItemOutcome(second, null, "400: no")])
            .Select(folded => folded.Result.Resolved).Should().BeEquivalentTo([true, false]);
    }

    [Fact]
    public void An_outcome_keeps_only_text_of_what_was_written()
    {
        var call = PlannedCalls.From([Outstanding(new Dictionary<string, string>())], 365).Single();
        var outcome = JsonSerializer.SerializeToElement(new { call, written = new { level = "low", depth = 3.2 } }, JsonSerializerOptions.Web);

        PlannedCalls.OutcomeOf(outcome, resolved: true).Written.Should().BeEquivalentTo(new Dictionary<string, string> { ["level"] = "low" });
    }

    [Fact]
    public void A_failed_outcome_giving_no_reason_says_so()
    {
        var call = PlannedCalls.From([Outstanding(new Dictionary<string, string>())], 365).Single();
        var outcome = JsonSerializer.SerializeToElement(new { call }, JsonSerializerOptions.Web);

        PlannedCalls.OutcomeOf(outcome, resolved: false).Reason.Should().Be("The call failed and its answer gave no reason.");
    }
}
