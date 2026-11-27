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
        var node = new WatershedNode(
            factory, NullLogger<WatershedNode>.Instance, MyceliumUrl, heights, new LandCoverReader(factory), clock, new LaunchToken("svc-jwt"));
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
            new GroundHeightReader(factory, NullLogger<GroundHeightReader>.Instance, MyceliumUrl, clock), new LandCoverReader(factory), clock);

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

    private static readonly string[] RunoffGrids =
        ["runoffClassOneInFiveGrid", "runoffClassOneInHundredGrid", "runoffClassOneInHundredBuiltOverGrid"];

    private static readonly double BorderLongitude = Border.Average(corner => corner.Longitude);

    private static double BowlAt(Position point) => 100 + 2e6 * (
        Math.Pow(point.Latitude - Border.Average(corner => corner.Latitude), 2) + Math.Pow(point.Longitude - BorderLongitude, 2));

    private static double?[] Values(JsonElement grid) =>
        [.. grid.GetProperty("values").EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Null ? (double?)null : value.GetDouble())];

    private static WatershedModel ABowlWithRunoffVocabulary(bool siteIsInAPlace = true, bool holdsRunoffClasses = true)
    {
        var model = new WatershedModel(Border) { HeightAt = BowlAt };
        model.HoldsRunoffVocabulary(siteIsInAPlace, holdsRunoffClasses);
        return model;
    }

    [Fact]
    public async Task A_site_in_a_place_with_a_rain_table_has_its_three_runoff_grids_written_laid_out_as_its_height_grid()
    {
        var model = ABowlWithRunoffVocabulary();
        model.HoldsRainTable(100);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        var written = SiteBatch(model);
        var heights = written["terrainHeightGrid"];
        foreach (var name in RunoffGrids)
        {
            var grid = written[name];
            foreach (var layout in new[] { "rows", "columns", "originLatitude", "originLongitude", "cellSizeMetres", "latitudeStepDegrees", "longitudeStepDegrees" })
                grid.GetProperty(layout).GetDouble().Should().Be(heights.GetProperty(layout).GetDouble(), layout);
            Values(grid).Where(value => value is not null).Should().NotBeEmpty()
                .And.OnlyContain(value => WatershedModel.RunoffGridCodes.Contains((long)value!.Value));
        }
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public async Task The_table_used_east_of_a_longitude_is_chosen_for_a_site_east_of_it_and_the_other_for_a_site_west_of_it(
        double degreesFromTheSite, bool eastTableChosen)
    {
        var model = ABowlWithRunoffVocabulary();
        model.HoldsRainTable(0.001);
        model.HoldsRainTable(500, appliesEastOfLongitude: BorderLongitude + degreesFromTheSite);

        await RunAsync(model);

        Values(SiteBatch(model)["runoffClassOneInHundredGrid"]).Contains(WatershedModel.StandingWaterGridCode).Should().Be(eastTableChosen);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task The_table_of_the_nearest_place_holding_one_is_used(bool theRegionHoldsATable, bool standingWater)
    {
        var model = ABowlWithRunoffVocabulary();
        if (theRegionHoldsATable) model.HoldsRainTable(0.001);
        model.HoldsRainTable(500, appliesIn: WatershedModel.Country);

        await RunAsync(model);

        Values(SiteBatch(model)["runoffClassOneInHundredGrid"]).Contains(WatershedModel.StandingWaterGridCode).Should().Be(standingWater);
    }

    [Theory]
    [InlineData("no place")]
    [InlineData("no table")]
    [InlineData("no runoff class")]
    public async Task Without_a_place_a_table_or_a_runoff_class_no_runoff_grid_is_written_and_the_study_is_still_stamped(string missing)
    {
        var model = ABowlWithRunoffVocabulary(siteIsInAPlace: missing != "no place", holdsRunoffClasses: missing != "no runoff class");
        if (missing != "no table") model.HoldsRainTable(100);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        SiteBatch(model).Keys.Should().Contain("terrainHeightGrid").And.NotContain(RunoffGrids);
        StampOf(model.Fragments.Single());
    }

    [Fact]
    public async Task A_corrected_runoff_input_on_the_study_changes_the_classes()
    {
        var model = ABowlWithRunoffVocabulary();
        model.HoldsRainTable(500);
        model.States(WatershedModel.Study, "openGroundRunoffCoefficient", 1e-9);

        await RunAsync(model);

        Values(SiteBatch(model)["runoffClassOneInHundredGrid"]).Should().NotContain(WatershedModel.StandingWaterGridCode);
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

    private static readonly string EastHalfAndBeyond =
        LayerGeometryTests.GeoJson("Polygon", new[] { LayerGeometryTests.Closed(At(0, -400), At(900, -400), At(900, 400), At(0, 400)) });

    private static double[] At(double eastMetres, double northMetres) => LayerGeometryTests.At(eastMetres, northMetres);

    private static Dictionary<string, JsonElement> FeatureBatch(WatershedModel model, Guid feature) =>
        model.Observed.Should().ContainSingle(batch => batch.Thing == feature).Subject.Values;

    [Fact]
    public async Task A_shape_over_half_the_site_has_its_hectares_inside_share_of_the_site_and_distance_written()
    {
        var model = new WatershedModel(Border);
        var feature = model.HoldsALayerFeature(EastHalfAndBeyond);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        var written = FeatureBatch(model, feature);
        written.Keys.Should().BeEquivalentTo(["hectaresInside", "shareOfSitePercent", "distanceFromSiteMetres"]);
        written["hectaresInside"].GetDouble().Should().BeApproximately(850 * 700 / 10000.0, 1e-6);
        written["shareOfSitePercent"].GetDouble().Should().BeApproximately(50, 1e-6);
        written["distanceFromSiteMetres"].GetDouble().Should().Be(0);
    }

    [Fact]
    public async Task A_line_has_only_its_metres_inside_whether_it_crosses_the_border_and_its_distance_written()
    {
        var model = new WatershedModel(Border);
        var feature = model.HoldsALayerFeature(LayerGeometryTests.GeoJson("LineString", new[] { At(-900, 0), At(900, 0) }));

        await RunAsync(model);

        var written = FeatureBatch(model, feature);
        written.Keys.Should().BeEquivalentTo(["metresInside", "crossesBorder", "distanceFromSiteMetres"]);
        written["metresInside"].GetDouble().Should().BeApproximately(1700, 1e-6);
        written["crossesBorder"].GetBoolean().Should().BeTrue();
        written["distanceFromSiteMetres"].GetDouble().Should().Be(0);
    }

    [Fact]
    public async Task A_feature_whose_geometry_cannot_be_read_is_left_unwritten_and_the_others_are_written()
    {
        var model = new WatershedModel(Border, bindsAHeightsSource: false);
        model.HoldsALayerFeature("""{"type":"Point","coordinates":[-30,40]}""");
        var readable = model.HoldsALayerFeature(EastHalfAndBeyond);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.Observed.Select(batch => batch.Thing).Should().Equal(readable);
    }

    [Fact]
    public async Task A_shape_the_site_has_of_a_kind_the_role_does_not_bind_is_not_measured()
    {
        var model = new WatershedModel(Border, bindsAHeightsSource: false);
        model.HoldsAShapeOfAnotherKind(EastHalfAndBeyond);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.Observed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_binding_no_heights_source_still_has_its_site_layer_features_worked_out_and_is_stamped()
    {
        var model = new WatershedModel(Border, bindsAHeightsSource: false);
        var feature = model.HoldsALayerFeature(EastHalfAndBeyond);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.HeightCalls.Should().Be(0);
        model.Observed.Select(batch => batch.Thing).Should().Equal(feature);
        StampOf(model.Fragments.Single());
    }

    [Fact]
    public async Task A_feature_batch_that_never_reads_back_fails_the_run_so_it_can_be_tried_again_and_writes_no_stamp()
    {
        var model = new WatershedModel(Border, bindsAHeightsSource: false) { DropsObservations = true };
        model.HoldsALayerFeature(EastHalfAndBeyond);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        model.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_border_enclosing_no_area_has_no_site_layer_feature_worked_out_and_is_stamped()
    {
        var model = new WatershedModel(Border, bindsAHeightsSource: false);
        model.StatesBoundary("""{"type":"Polygon","coordinates":[[[-30,40],[-29.99,40],[-29.98,40],[-30,40]]]}""");
        model.HoldsALayerFeature(EastHalfAndBeyond);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.Observed.Should().BeEmpty();
        StampOf(model.Fragments.Single());
    }

    private const double BorderHectares = 1700 * 700 / 10000.0;
    private static readonly string[] TilesTheBorderTouches = ["N39W033", "N39W030"];

    private static byte TreesWestBareEast(Position centre) => centre.Longitude < MadeUpBorders.Longitude ? (byte)10 : (byte)60;

    private static WatershedModel CoveringLand(bool bindsAHeightsSource = true, bool bindsTheRaster = true, Func<BuiltTile, BuiltTile>? laidOut = null)
    {
        var model = new WatershedModel(Border, bindsAHeightsSource);
        model.CoversLand(bindsTheRaster);
        foreach (var name in TilesTheBorderTouches)
            model.LandCoverTiles[name] = (laidOut ?? (built => built))(BuiltTile.Named(name).Over(Border, TreesWestBareEast)).Build().Bytes;
        return model;
    }

    private static Dictionary<string, JsonElement> ShareBatch(WatershedModel model, int code) =>
        model.Observed.Should().ContainSingle(batch => batch.Thing == model.LandCoverShareOf(code)).Subject.Values;

    [Fact]
    public async Task Each_land_cover_share_is_written_with_its_pixel_count_area_and_share_of_the_land_inside_the_border()
    {
        var model = CoveringLand();

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        foreach (var code in WatershedModel.LandCoverCodes)
            ShareBatch(model, code).Keys.Should().BeEquivalentTo("pixelCount", "sharePercent", "areaHectares");
        var (trees, bare) = (ShareBatch(model, 10), ShareBatch(model, 60));
        (trees["sharePercent"].GetDouble() + bare["sharePercent"].GetDouble()).Should().BeApproximately(100, 1e-9);
        trees["sharePercent"].GetDouble().Should().BeApproximately(50, 1, "the border is split evenly at the line between the two tiles");
        (trees["areaHectares"].GetDouble() + bare["areaHectares"].GetDouble()).Should().BeApproximately(BorderHectares, BorderHectares / 100);
        WatershedModel.LandCoverCodes.Where(code => code is not (10 or 60))
            .Should().OnlyContain(code => ShareBatch(model, code)["pixelCount"].GetInt32() == 0 && ShareBatch(model, code)["sharePercent"].GetDouble() == 0);
    }

    [Fact]
    public async Task The_land_cover_is_read_after_the_terrain_is_recorded()
    {
        var model = CoveringLand();

        await RunAsync(model);

        model.Asked.IndexOf($"observations {WatershedModel.Site}").Should()
            .BeLessThan(model.Asked.FindIndex(asked => asked.StartsWith("land cover", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_land_cover_batch_that_never_reads_back_fails_the_run_so_it_can_be_tried_again_and_writes_no_stamp()
    {
        var model = CoveringLand(bindsAHeightsSource: false);
        model.DropsObservations = true;

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        model.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_binding_no_land_cover_raster_asks_for_no_tile_writes_no_share_and_is_stamped()
    {
        var model = CoveringLand(bindsTheRaster: false);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.LandCoverCalls.Should().Be(0);
        model.Observed.Should().NotContain(batch => model.LandCoverShares.Contains(batch.Thing));
        StampOf(model.Fragments.Single());
    }

    [Fact]
    public async Task A_place_the_source_holds_no_tile_for_writes_no_share_and_is_stamped()
    {
        var model = CoveringLand();
        model.LandCoverTiles.Clear();

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.LandCoverCalls.Should().Be(TilesTheBorderTouches.Length);
        model.Observed.Should().NotContain(batch => model.LandCoverShares.Contains(batch.Thing));
        StampOf(model.Fragments.Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(null)]
    public async Task A_land_cover_source_that_cannot_be_reached_or_answers_later_can_be_tried_again_and_the_terrain_written_stays(HttpStatusCode? status)
    {
        var model = CoveringLand();
        if (status is { } answered) model.LandCoverStatus = answered;
        else model.LandCoverUnreachable = true;

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        model.Fragments.Should().BeEmpty();
        SiteBatch(model).Should().ContainKey("terrainHeightGrid");
    }

    [Fact]
    public async Task A_tile_in_another_layout_fails_the_run_naming_what_differs_and_writes_no_stamp()
    {
        var model = CoveringLand(laidOut: built => built with { Compression = 5 });

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().BeNull();
        response.Error.Should().Contain("compression 5");
        model.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_source_that_answers_a_byte_range_with_the_whole_file_fails_the_run_and_writes_no_stamp()
    {
        var model = CoveringLand();
        model.LandCoverStatus = HttpStatusCode.OK;

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().BeNull();
        response.Error.Should().Contain("byte range");
        model.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_binding_no_heights_source_still_has_its_land_cover_worked_out_and_is_stamped()
    {
        var model = CoveringLand(bindsAHeightsSource: false);

        var response = await RunAsync(model);

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        model.HeightCalls.Should().Be(0);
        model.Observed.Select(batch => batch.Thing).Should().BeEquivalentTo(model.LandCoverShares);
        StampOf(model.Fragments.Single());
    }
}
