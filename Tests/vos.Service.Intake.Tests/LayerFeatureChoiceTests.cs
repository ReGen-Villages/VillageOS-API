using FluentAssertions;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

public class LayerFeatureChoiceTests
{
    private const string Outline = """{"type":"Polygon","coordinates":[[[8.1,47.2],[8.2,47.2],[8.2,47.3],[8.1,47.2]]]}""";

    private static Refusal RefusalOf(Action choosing) =>
        choosing.Should().Throw<SubmissionError>().Which.AsRefusal();

    [Fact]
    public void Each_name_is_read_with_the_kind_chosen_for_it()
    {
        LayerFeatureChoice.Read("""{" Mill Brook ":" brook-or-wadi ","Zone":"flood-extent"}""")
            .Should().Equal(new Dictionary<string, string> { ["Mill Brook"] = "brook-or-wadi", ["Zone"] = "flood-extent" });
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""["Mill Brook"]""")]
    [InlineData("""{"Mill Brook":3}""")]
    [InlineData("""{"Mill Brook":" "}""")]
    [InlineData("""{" ":"flood-extent"}""")]
    [InlineData("""{"Mill Brook":"flood-extent","Mill Brook ":"other-layer"}""")]
    public void A_field_that_is_not_one_kind_for_each_name_is_refused(string field)
    {
        RefusalOf(() => LayerFeatureChoice.Read(field)).Code.Should().Be(RefusalCode.FeaturesUnreadable);
    }

    [Fact]
    public void A_name_longer_than_a_name_holds_is_refused()
    {
        var refusal = RefusalOf(() => LayerFeatureChoice.Read(
            $$"""{"{{new string('a', SubmissionLimits.LongestText + 1)}}":"other-layer"}"""));

        refusal.Code.Should().Be(RefusalCode.FieldTooLong);
        refusal.Values!["most"].Should().Be(SubmissionLimits.LongestText);
    }

    [Fact]
    public void A_name_the_file_does_not_hold_is_refused_by_that_name()
    {
        var refusal = RefusalOf(() => LayerFeatureChoice.Choose(
            [new LayerShape("Mill Brook", Outline)], new Dictionary<string, string> { ["Zone"] = "flood-extent" }));

        refusal.Code.Should().Be(RefusalCode.FeatureNotInFile);
        refusal.Values!["value"].Should().Be("Zone");
    }

    [Fact]
    public void Every_shape_bearing_a_chosen_name_is_chosen_and_a_shape_not_chosen_is_not()
    {
        var chosen = LayerFeatureChoice.Choose(
            [new LayerShape("Zone", Outline), new LayerShape("Mill Brook", Outline), new LayerShape("Zone", Outline)],
            new Dictionary<string, string> { ["Zone"] = "flood-extent" });

        chosen.Should().HaveCount(2).And.OnlyContain(feature => feature.Name == "Zone" && feature.Kind == "flood-extent");
    }

    [Fact]
    public void More_features_than_one_file_may_store_are_refused()
    {
        var shapes = Enumerable.Range(0, SubmissionLimits.MostLayerFeatures + 1)
            .Select(_ => new LayerShape("Zone", Outline)).ToList();

        var refusal = RefusalOf(() => LayerFeatureChoice.Choose(shapes, new Dictionary<string, string> { ["Zone"] = "flood-extent" }));

        refusal.Code.Should().Be(RefusalCode.FieldTooMany);
        refusal.Values!["field"].Should().Be(LayerFeatureChoice.FormField);
    }

    [Fact]
    public void As_many_features_as_one_file_may_store_are_taken()
    {
        var shapes = Enumerable.Range(0, SubmissionLimits.MostLayerFeatures)
            .Select(_ => new LayerShape("Zone", Outline)).ToList();

        LayerFeatureChoice.Choose(shapes, new Dictionary<string, string> { ["Zone"] = "flood-extent" })
            .Should().HaveCount(SubmissionLimits.MostLayerFeatures);
    }
}
