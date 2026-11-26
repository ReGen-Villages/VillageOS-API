using System.Text.Json;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Tests;

// The reference parcel's heights recorded once from Open Topo Data, and what the site-study demo's own code made of them
// at its whole-metre cell and at the true cell. The file holds no position, so the public repository does not place the site.
public sealed record ReferenceParcel(
    int Columns, int Rows, double CellSizeMetres, double[] Heights, bool[] Inside,
    DemoAnswer DemoAtWholeMetreCell, DemoAnswerWithGrids DemoAtTrueCell, ReferenceProfiles Profiles)
{
    public static ReferenceParcel Recorded { get; } = JsonSerializer.Deserialize<ReferenceParcel>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "reference-parcel.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public TerrainSurface Surface => new(Columns, Rows, CellSizeMetres, Heights, Inside);

    public static double AsTheDemoRounds(double value) => Math.Round(value * 10, MidpointRounding.AwayFromZero) / 10;
}

public record DemoAnswer(DemoStatistics Stats, DemoSlopeClass[] SlopeClasses, DemoRose Rose);

public sealed record DemoAnswerWithGrids(
    DemoStatistics Stats, DemoSlopeClass[] SlopeClasses, DemoRose Rose,
    double[] SlopePercent, double[] AspectDegrees, double[] DrainageCells, double[] HollowDepthMetres)
    : DemoAnswer(Stats, SlopeClasses, Rose);

public sealed record DemoStatistics(
    int Cells, double AreaHa, double Min, double Max, double Mean, double SlopeMean, double SlopeMedian, double SlopeP90,
    int DepressionCells, double DepressionHa, DemoLowest Lowest, double CellHa);

public sealed record DemoLowest(int K, double Z);

public sealed record DemoSlopeClass(string Id, double? Max, double Pct, double Ha);

public sealed record DemoRose(string[] Dirs, double[] Pct, double FlatPct);

public sealed record ReferenceProfiles(ReferenceProfile WestToEast, ReferenceProfile SouthToNorth);

public sealed record ReferenceProfile(double SpacingMetres, double SiteFromMetres, double SiteToMetres, double[] Heights);
