using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Watershed.Services;
using vos.Service.Watershed.Terrain;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class WatershedNodeTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly Guid Run = Guid.Parse("81970000-0000-0000-0000-0000000000aa");
    private static readonly Guid Node = Guid.Parse("81970000-0000-0000-0000-0000000000bb");
    private static readonly IReadOnlyList<Position> Border = MadeUpBorders.Rectangle(1700, 700);

    private static readonly string[] SiteGrids =
        ["terrainHeightGrid", "terrainSlopeGrid", "terrainAspectGrid", "drainageAreaGrid", "hollowDepthGrid", "terrainProfileWestToEast", "terrainProfileSouthToNorth"];

    private static readonly string[] SiteFigures =
    [
        "westToEastSpanFromMetres", "westToEastSpanToMetres", "southToNorthSpanFromMetres", "southToNorthSpanToMetres",
        "terrainAreaHectares", "terrainLowestMetres", "terrainHighestMetres", "terrainMeanMetres", "slopeMeanPercent",
        "slopeMedianPercent", "slopeNinetiethPercentilePercent", "hollowHectares", "lowestPointLatitude", "lowestPointLongitude",
    ];

    private static async Task<NodeResponse> RunAsync(WatershedModel model, string? subject = null)
    {
        var factory = new PerCallHttpClientFactory(new MockHttpMessageHandler(model.Answer));
        var clock = new ClockThatWaitsNoTime();
        var heights = new GroundHeightReader(factory, NullLogger<GroundHeightReader>.Instance, MyceliumUrl, clock, new LaunchToken("svc-jwt"));
        var node = new WatershedNode(factory, NullLogger<WatershedNode>.Instance, MyceliumUrl, heights, clock, new LaunchToken("svc-jwt"));
        return await node.HandleNodeAsync(JsonDocument.Parse(
            $$$"""{"runId":"{{{Run}}}","nodeId":"{{{Node}}}","params":{},"inputs":{"subject":{{{subject ?? $"\"{WatershedModel.Study}\""}}}}}""").RootElement);
    }

    private static Dictionary<string, JsonElement> SiteBatch(WatershedModel model) =>
        model.Observed.Should().ContainSingle(batch => batch.Thing == WatershedModel.Site).Subject.Values;

    private static TerrainAnalysisResult Expected(WatershedModel model)
    {
        var layout = HeightGridLayout.Over(Border);
        return TerrainAnalysis.Of(new TerrainSurface(
            layout.Columns, layout.Rows, layout.CellSizeMetres, [.. model.Answered.Take(layout.Columns * layout.Rows)], layout.Inside));
    }

    private static JsonElement StampOf(JsonElement fragment) =>
        fragment.GetProperty("Things").EnumerateArray().Single(thing => thing.GetProperty("Id").GetGuid() == WatershedModel.Study)
            .GetProperty("Properties").GetProperty(WatershedNode.StampProperty);

    [Fact]
    public void The_step_reads_and_answers_the_study_on_its_subject_port()
    {
        var model = new WatershedModel(Border);
        var factory = new PerCallHttpClientFactory(new MockHttpMessageHandler(model.Answer));
        var clock = new ClockThatWaitsNoTime();
        var node = new WatershedNode(factory, NullLogger<WatershedNode>.Instance, MyceliumUrl,
            new GroundHeightReader(factory, NullLogger<GroundHeightReader>.Instance, MyceliumUrl, clock), clock);

        node.Ports.Select(port => (port.PortName, port.Direction)).Should().Equal(("subject", "in"), ("subject", "out"));
    }

    [Fact]
    public async Task A_bordered_study_has_its_sites_grids_profiles_and_figures_written_as_one_batch_of_observations()
    {
        var model = new WatershedModel(Border);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        var written = SiteBatch(model);
        written.Keys.Should().BeEquivalentTo([.. SiteGrids, .. SiteFigures]);
        var figures = Expected(model).Figures;
        written["terrainMeanMetres"].GetDouble().Should().BeApproximately(figures.MeanMetres, 1e-9);
        written["slopeMeanPercent"].GetDouble().Should().BeApproximately(figures.SlopeMeanPercent, 1e-9);
        written["terrainAreaHectares"].GetDouble().Should().BeApproximately(figures.AreaHectares, 1e-9);
    }

    [Fact]
    public async Task A_height_grid_is_written_from_the_south_west_with_its_layout_and_one_value_a_point()
    {
        var model = new WatershedModel(Border);

        await RunAsync(model);

        var layout = HeightGridLayout.Over(Border);
        var grid = SiteBatch(model)["terrainHeightGrid"];
        grid.GetProperty("rows").GetInt32().Should().Be(layout.Rows);
        grid.GetProperty("columns").GetInt32().Should().Be(layout.Columns);
        grid.GetProperty("cellSizeMetres").GetDouble().Should().BeApproximately(layout.CellSizeMetres, 1e-9);
        grid.GetProperty("originLatitude").GetDouble().Should().BeApproximately(layout.OriginLatitude, 1e-12);
        grid.GetProperty("originLongitude").GetDouble().Should().BeApproximately(layout.OriginLongitude, 1e-12);
        grid.GetProperty("latitudeStepDegrees").GetDouble().Should().BeApproximately(layout.LatitudeStepDegrees, 1e-15);
        grid.GetProperty("values").EnumerateArray().Select(value => value.GetDouble())
            .Should().Equal(model.Answered.Take(layout.Columns * layout.Rows).Select(height => (double)(float)height));
    }

    [Fact]
    public async Task Flat_ground_has_no_aspect_and_its_cell_is_written_unknown()
    {
        var model = new WatershedModel(Border) { HeightAt = _ => 50 };

        await RunAsync(model);

        SiteBatch(model)["terrainAspectGrid"].GetProperty("values").EnumerateArray()
            .Should().OnlyContain(value => value.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Each_share_is_written_to_the_share_thing_whose_term_holds_it()
    {
        var model = new WatershedModel(Border) { HeightAt = _ => 50 };

        await RunAsync(model);

        double ShareOf(Guid share) => model.Observed.Single(batch => batch.Thing == share).Values["sharePercent"].GetDouble();
        model.SlopeClassShares.Select(ShareOf).Should().Equal(100, 0, 0, 0, 0);
        model.AspectShares.Select(ShareOf).Should().Equal(0, 0, 0, 0, 0, 0, 0, 0, 100);
        model.Observed.Single(batch => batch.Thing == model.SlopeClassShares[0]).Values["areaHectares"].GetDouble()
            .Should().BeApproximately(Expected(model).Figures.AreaHectares, 1e-9);
    }

    [Fact]
    public async Task The_stamp_is_written_on_the_study_last_after_every_batch_reads_back()
    {
        var model = new WatershedModel(Border);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.Asked.TakeLast(3).Should().Equal(["snapshot", "time", "fragment"], "the stamp waits for the values to be read back");
        StampOf(model.Fragments.Single()).GetProperty("typeInfo").GetString().Should().Be("vos.DateTime");
        model.Observed.Should().HaveCount(1 + model.SlopeClassShares.Count + model.AspectShares.Count);
    }

    [Fact]
    public async Task The_stamp_is_the_models_now_not_the_wall_clocks()
    {
        var model = new WatershedModel(Border);

        await RunAsync(model);

        StampOf(model.Fragments.Single()).GetProperty("value").GetDateTimeOffset().Should().Be(WatershedModel.ModelNow);
    }

    [Fact]
    public async Task A_model_clock_that_cannot_be_read_can_be_tried_again_and_writes_no_stamp()
    {
        var model = new WatershedModel(Border) { TimeStatus = HttpStatusCode.ServiceUnavailable };

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        model.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_batch_that_never_reads_back_fails_the_run_so_it_can_be_tried_again_and_writes_no_stamp()
    {
        var model = new WatershedModel(Border) { DropsObservations = true };

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        model.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_study_whose_site_has_no_border_is_stamped_and_nothing_is_asked_or_written()
    {
        var model = new WatershedModel(border: null);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.HeightCalls.Should().Be(0);
        model.Observed.Should().BeEmpty();
        StampOf(model.Fragments.Single());
    }

    [Fact]
    public async Task A_model_binding_no_heights_source_is_stamped_and_nothing_is_asked_or_written()
    {
        var model = new WatershedModel(Border, bindsAHeightsSource: false);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.HeightCalls.Should().Be(0);
        model.Observed.Should().BeEmpty();
        StampOf(model.Fragments.Single());
    }

    [Fact]
    public async Task The_heights_are_asked_of_the_source_the_role_binds_for_the_grid_and_then_the_two_profiles()
    {
        var model = new WatershedModel(Border);

        await RunAsync(model);

        var layout = HeightGridLayout.Over(Border);
        model.HeightCalls.Should().Be((int)Math.Ceiling(layout.Columns * layout.Rows / 100.0) + 1);
    }

    [Fact]
    public async Task A_border_unchanged_since_the_stored_grids_reuses_their_heights_and_asks_for_none()
    {
        var first = new WatershedModel(Border);
        await RunAsync(first);
        var written = SiteBatch(first);
        var again = new WatershedModel(Border) { HeightAt = _ => throw new InvalidOperationException("no height is asked for") };
        foreach (var grid in new[] { "terrainHeightGrid", "terrainProfileWestToEast", "terrainProfileSouthToNorth" })
            again.Holds(WatershedModel.Site, grid, written[grid]);

        var response = await RunAsync(again);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        again.HeightCalls.Should().Be(0);
        SiteBatch(again)["terrainMeanMetres"].GetDouble().Should().BeApproximately(written["terrainMeanMetres"].GetDouble(), 1e-4);
    }

    [Fact]
    public async Task A_stored_grid_laid_out_for_another_border_is_asked_again()
    {
        var first = new WatershedModel(MadeUpBorders.Rectangle(1600, 700));
        await RunAsync(first);
        var again = new WatershedModel(Border);
        again.Holds(WatershedModel.Site, "terrainHeightGrid", SiteBatch(first)["terrainHeightGrid"]);

        await RunAsync(again);

        again.HeightCalls.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("""{"originLatitude":0,"originLongitude":0,"cellSizeMetres":10,"rows":1,"columns":1,"values":[1]}""")]
    [InlineData("""{"originLatitude":0,"originLongitude":0,"cellSizeMetres":10,"latitudeStepDegrees":0.0001,"longitudeStepDegrees":0.0001,"rows":"ten","columns":1,"values":[1]}""")]
    [InlineData("""{"originLatitude":0,"originLongitude":0,"cellSizeMetres":10,"latitudeStepDegrees":0.0001,"longitudeStepDegrees":0.0001,"rows":1.5,"columns":1,"values":[1]}""")]
    public async Task A_stored_grid_that_cannot_be_read_is_asked_again(string stored)
    {
        var model = new WatershedModel(Border);
        model.Holds(WatershedModel.Site, "terrainHeightGrid", JsonDocument.Parse(stored).RootElement);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.HeightCalls.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("LineString")]
    [InlineData("MultiPolygon")]
    [InlineData(null)]
    public async Task A_border_stated_as_a_shape_other_than_a_polygon_is_no_border_and_the_study_is_stamped(string? type)
    {
        var shape = new Dictionary<string, object>
        {
            ["coordinates"] = new[] { Border.Append(Border[0]).Select(corner => new[] { corner.Longitude, corner.Latitude }).ToArray() },
        };
        if (type is not null) shape["type"] = type;
        var model = new WatershedModel(Border);
        model.StatesBoundary(JsonSerializer.Serialize(shape));

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.HeightCalls.Should().Be(0);
        model.Observed.Should().BeEmpty();
        StampOf(model.Fragments.Single());
    }

    [Theory]
    [InlineData("""{"type":"Polygon","coordinates":[]}""")]
    [InlineData("\"not a polygon\"")]
    [InlineData("""{"type":"Polygon","coordinates":[[[0],[1],[2]]]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[[["a","b"],["c","d"],["e","f"]]]}""")]
    public async Task A_border_that_cannot_be_read_as_a_polygon_is_no_border_and_the_study_is_stamped(string stated)
    {
        var model = new WatershedModel(Border);
        model.StatesBoundary(stated);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.HeightCalls.Should().Be(0);
        model.Observed.Should().BeEmpty();
        StampOf(model.Fragments.Single());
    }

    [Fact]
    public async Task A_share_of_a_kind_no_role_binds_is_left_unwritten()
    {
        var model = new WatershedModel(Border);
        var other = model.HoldsAShareOfAnotherKind();

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.Observed.Should().NotContain(batch => batch.Thing == other);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_batch_the_model_cannot_take_now_can_be_tried_again_and_writes_no_stamp(HttpStatusCode status)
    {
        var model = new WatershedModel(Border) { ObservationsStatus = status };

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        model.Fragments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_batch_the_model_refuses_fails_the_run_and_writes_no_stamp(HttpStatusCode status)
    {
        var model = new WatershedModel(Border) { ObservationsStatus = status };

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().BeNull();
        model.Fragments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_model_that_refuses_the_read_fails_the_run(HttpStatusCode status)
    {
        var response = await RunAsync(new WatershedModel(Border) { SnapshotStatus = status });

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().BeNull();
    }

    [Fact]
    public async Task A_subject_naming_a_study_the_model_does_not_hold_fails_the_run()
    {
        var model = new WatershedModel(Border);

        var response = await RunAsync(model, $"\"{Guid.NewGuid()}\"");

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().BeNull();
        model.Fragments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_model_that_cannot_be_read_can_be_tried_again(HttpStatusCode status)
    {
        var response = await RunAsync(new WatershedModel(Border) { SnapshotStatus = status });

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
    }

    [Fact]
    public async Task A_height_source_at_its_limit_can_be_tried_again_and_nothing_is_written()
    {
        var model = new WatershedModel(Border) { HeightsStatus = HttpStatusCode.TooManyRequests };

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        model.Observed.Should().BeEmpty();
        model.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_stamp_the_model_cannot_take_now_can_be_tried_again()
    {
        var response = await RunAsync(new WatershedModel(border: null) { FragmentStatus = HttpStatusCode.ServiceUnavailable });

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"not-a-thing\"")]
    public async Task A_subject_naming_no_study_fails_the_run(string subject)
    {
        var response = await RunAsync(new WatershedModel(Border), subject);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().BeNull();
    }
}
