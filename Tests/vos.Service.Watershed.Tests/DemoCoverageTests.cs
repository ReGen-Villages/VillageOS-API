using System.Text.Json;
using FluentAssertions;
using vos.Service.Watershed.SiteLayers;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

public sealed record DemoCoverage(
    string Name, double[][] Site, string Type, double[][] Points, double SiteHectares,
    double? HectaresInside, double? ShareOfSitePercent, double? MetresInside, bool? CrossesBorder, double DistanceFromSiteMetres)
{
    public static IReadOnlyList<DemoCoverage> Recorded { get; } = JsonSerializer.Deserialize<DemoCoverage[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "demo-coverage.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public override string ToString() => Name;
}

// The recorded figures are the site-study pages' own site-layers.js, run under Node. It samples the site on a grid of points
// at least 2 m apart and each line in pieces of at most 5 m.
public class DemoCoverageTests
{
    private const double SampledLinePieceMetres = 5;

    private static readonly string[] MeasuredByTheDemoOnlyFromTheLinesOwnPoints = ["a line passing a corner"];

    public static TheoryData<DemoCoverage> Fixtures
    {
        get
        {
            var fixtures = new TheoryData<DemoCoverage>();
            foreach (var fixture in DemoCoverage.Recorded)
                fixtures.Add(fixture);
            return fixtures;
        }
    }

    private static IReadOnlyList<Position> Positions(double[][] latitudesAndLongitudes) =>
        [.. latitudesAndLongitudes.Select(pair => new Position(pair[0], pair[1]))];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Each_figure_agrees_with_the_site_study_pages_within_their_sampling(DemoCoverage demo)
    {
        var border = Positions(demo.Site);
        var frame = LocalFrame.Around(border);
        var site = new PlaneShape([new PlanePolygon([.. border.Select(frame.At)], [])]);
        var points = Positions(demo.Points).Select(frame.At).ToArray();

        if (demo.Type == "polygon")
        {
            var covered = SiteLayerCoverage.OfShape(site, new PlaneShape([new PlanePolygon(points, [])]));
            covered.HectaresInside.Should().BeApproximately(demo.HectaresInside!.Value, 0.01 * demo.SiteHectares);
            covered.ShareOfSitePercent.Should().BeApproximately(demo.ShareOfSitePercent!.Value, 1);
            covered.DistanceFromSiteMetres.Should().BeApproximately(demo.DistanceFromSiteMetres, 1e-6);
            return;
        }

        var line = SiteLayerCoverage.OfLines(site, [new PlaneLine(points)]);
        line.MetresInside.Should().BeApproximately(demo.MetresInside!.Value, SampledLinePieceMetres);
        line.CrossesBorder.Should().Be(demo.CrossesBorder!.Value);
        if (MeasuredByTheDemoOnlyFromTheLinesOwnPoints.Contains(demo.Name))
            line.DistanceFromSiteMetres.Should().BeLessThan(demo.DistanceFromSiteMetres);
        else
            line.DistanceFromSiteMetres.Should().BeApproximately(demo.DistanceFromSiteMetres, 1e-6);
    }
}
