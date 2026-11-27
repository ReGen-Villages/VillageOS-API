namespace vos.Service.Stomata.Services;

public sealed class ZoneOutlines(DesignStrategies strategies, double hottestCelsius, double mostHumidGramsPerKilogram)
{
    public string ComfortInStillAir() => Empty;

    public string ComfortWithAirMovement() => Empty;

    public string? DirectEvaporativeCooling() => Empty;

    public string? TwoStageEvaporativeCooling() => Empty;

    private const string Empty = """{"type":"Polygon","coordinates":[[]]}""";
}
