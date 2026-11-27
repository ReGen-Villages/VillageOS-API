using System.Globalization;
using System.Text;

namespace vos.Service.Stomata.Services;

// Each outline is a GeoJSON Polygon in the psychrometric chart's own units, air temperature across and humidity ratio up,
// written with a fixed corner order and rounding so that the same inputs give the same text.
public sealed class ZoneOutlines(
    DesignStrategies strategies, IReadOnlyCollection<(double Celsius, double HumidityRatioGramsPerKilogram)> observed)
{
    public const string EmptyPolygon = """{"type":"Polygon","coordinates":[]}""";

    private const double HumidityStep = 0.5;
    private const double TemperatureStep = 0.25;

    public string ComfortInStillAir() => Comfort(strategies.Assumptions.StillAirMetresPerSecond);

    public string ComfortWithAirMovement() => Comfort(strategies.Assumptions.AirMovementMetresPerSecond);

    public string DirectEvaporativeCooling() => Evaporative(strategies.DirectEvaporationWorks);

    public string TwoStageEvaporativeCooling() => Evaporative(strategies.TwoStageEvaporationWorks);

    private string Comfort(double airSpeedMetresPerSecond)
    {
        var rows = HumidityRows(0, strategies.Assumptions.HumidityRatioLimitGramsPerKilogram)
            .Select(humidityRatio =>
            {
                var (cool, warm) = strategies.ZoneAt(humidityRatio, airSpeedMetresPerSecond);
                return (humidityRatio, cool, warm);
            })
            .ToList();
        return Polygon(rows);
    }

    // An empty outline means this cooling brings no air within the range the site recorded into the summer zone.
    private string Evaporative(Func<double, double, bool> works)
    {
        if (observed.Count == 0) return EmptyPolygon;

        var hottestCelsius = observed.Max(hour => hour.Celsius);
        var rows = new List<(double HumidityRatio, double Cool, double Warm)>();
        foreach (var humidityRatio in HumidityRows(HumidityStep, observed.Max(hour => hour.HumidityRatioGramsPerKilogram)))
        {
            var reached = Temperatures(hottestCelsius)
                .Where(celsius => Psychrometrics.RelativeHumidityPercent(celsius, humidityRatio, strategies.Assumptions.PressureKilopascals) < 100
                                  && strategies.IsWarmerThanTheSummerZone(celsius, humidityRatio)
                                  && works(celsius, humidityRatio))
                .ToList();
            if (reached.Count > 0) rows.Add((humidityRatio, reached[0], reached[^1]));
        }
        return rows.Count > 1 ? Polygon(rows) : EmptyPolygon;
    }

    private static IEnumerable<double> Temperatures(double hottestCelsius)
    {
        for (var step = 0; step * TemperatureStep <= hottestCelsius; step++) yield return step * TemperatureStep;
    }

    private static IEnumerable<double> HumidityRows(double from, double to)
    {
        var step = 0;
        for (; step * HumidityStep <= to; step++)
            if (step * HumidityStep >= from) yield return step * HumidityStep;
        if (to >= from && (step - 1) * HumidityStep < to) yield return to;
    }

    // Counter-clockwise from the cool, dry corner: along the driest row, up the warm edge, then down the cool edge.
    private static string Polygon(IReadOnlyList<(double HumidityRatio, double Cool, double Warm)> rows)
    {
        var corners = new List<(double Celsius, double HumidityRatio)> { (rows[0].Cool, rows[0].HumidityRatio) };
        corners.AddRange(rows.Select(row => (row.Warm, row.HumidityRatio)));
        corners.AddRange(rows.Reverse().Select(row => (row.Cool, row.HumidityRatio)));

        var text = new StringBuilder("""{"type":"Polygon","coordinates":[[""");
        text.AppendJoin(',', corners.Select(corner => $"[{Rounded(corner.Celsius)},{Rounded(corner.HumidityRatio)}]"));
        return text.Append("]]}").ToString();
    }

    private static string Rounded(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);
}
