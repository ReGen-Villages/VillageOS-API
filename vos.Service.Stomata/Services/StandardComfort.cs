namespace vos.Service.Stomata.Services;

// ASHRAE 55-2017: Fanger's predicted mean vote in still air, and above 0.2 m/s the cooling effect, the drop in air and radiant
// temperature at which still air gives the same standard effective temperature (SET), worked by the two-node model.
public sealed class StandardComfort : IThermalComfort
{
    public const double StillAirMetresPerSecond = 0.1;
    public const double ElevatedAirSpeedAboveMetresPerSecond = 0.2;

    private const double WattsPerSquareMetrePerMet = 58.15;
    private const double SquareMetreKelvinPerWattPerClo = 0.155;
    private const double LargestCoolingEffectCelsius = 40;

    public double PredictedMeanVote(
        double airCelsius, double relativeHumidityPercent, double clothingClo, double airSpeedMetresPerSecond, double metabolicRateMet,
        double pressureKilopascals)
    {
        if (airSpeedMetresPerSecond <= ElevatedAirSpeedAboveMetresPerSecond)
            return FangerPredictedMeanVote(airCelsius, airCelsius, relativeHumidityPercent, airSpeedMetresPerSecond, metabolicRateMet, clothingClo);
        var cooling = CoolingEffectCelsius(
            airCelsius, airCelsius, relativeHumidityPercent, airSpeedMetresPerSecond, metabolicRateMet, clothingClo, pressureKilopascals);
        return FangerPredictedMeanVote(
            airCelsius - cooling, airCelsius - cooling, relativeHumidityPercent, StillAirMetresPerSecond, metabolicRateMet, clothingClo);
    }

    public static double FangerPredictedMeanVote(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo)
    {
        var vapourPascals = relativeHumidityPercent * 10 * Math.Exp(16.6536 - 4030.183 / (airCelsius + 235));
        var clothingInsulation = SquareMetreKelvinPerWattPerClo * clothingClo;
        var metabolic = metabolicRateMet * WattsPerSquareMetrePerMet;
        var clothingAreaFactor = clothingInsulation <= 0.078 ? 1 + 1.29 * clothingInsulation : 1.05 + 0.645 * clothingInsulation;
        var forcedConvection = 12.1 * Math.Sqrt(airSpeedMetresPerSecond);
        var airKelvin = airCelsius + 273;
        var radiantKelvin = meanRadiantCelsius + 273;

        var insulatedArea = clothingInsulation * clothingAreaFactor;
        var radiantGain = 308.7 - 0.028 * metabolic + insulatedArea * 3.96 * Math.Pow(radiantKelvin / 100, 4);
        var surfaceHundredthsOfKelvin = (airKelvin + (35.5 - airCelsius) / (3.5 * clothingInsulation + 0.1)) / 100;
        var previous = surfaceHundredthsOfKelvin * 2;
        var convection = forcedConvection;
        for (var step = 0; Math.Abs(surfaceHundredthsOfKelvin - previous) > 0.00015; step++)
        {
            if (step == 150) throw new InvalidOperationException("Fanger's clothing surface temperature did not settle.");
            previous = (previous + surfaceHundredthsOfKelvin) / 2;
            convection = Math.Max(forcedConvection, 2.38 * Math.Pow(Math.Abs(100 * previous - airKelvin), 0.25));
            surfaceHundredthsOfKelvin = (radiantGain + insulatedArea * airKelvin * convection - insulatedArea * 3.96 * Math.Pow(previous, 4))
                                        / (100 + insulatedArea * 100 * convection);
        }
        var clothingSurfaceCelsius = 100 * surfaceHundredthsOfKelvin - 273;

        var skinDiffusion = 3.05e-3 * (5733 - 6.99 * metabolic - vapourPascals);
        var sweating = metabolic > WattsPerSquareMetrePerMet ? 0.42 * (metabolic - WattsPerSquareMetrePerMet) : 0;
        var latentRespiration = 1.7e-5 * metabolic * (5867 - vapourPascals);
        var dryRespiration = 0.0014 * metabolic * (34 - airCelsius);
        var radiation = 3.96 * clothingAreaFactor * (Math.Pow(surfaceHundredthsOfKelvin, 4) - Math.Pow(radiantKelvin / 100, 4));
        var convected = clothingAreaFactor * convection * (clothingSurfaceCelsius - airCelsius);
        var sensitivity = 0.303 * Math.Exp(-0.036 * metabolic) + 0.028;
        return sensitivity * (metabolic - skinDiffusion - sweating - latentRespiration - dryRespiration - radiation - convected);
    }

    public static double StandardEffectiveTemperatureCelsius(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo, double pressureKilopascals) =>
        StandardEffectiveTemperatureCelsius(
            airCelsius, meanRadiantCelsius, relativeHumidityPercent, airSpeedMetresPerSecond, metabolicRateMet, clothingClo, pressureKilopascals,
            ActivityConvection(metabolicRateMet));

    // The method works the cooling effect without the convection the body's own activity makes.
    private static double StandardEffectiveTemperatureCelsius(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo, double pressureKilopascals, double activityConvection)
    {
        const double bodyKilograms = 69.9;
        const double bodySquareMetres = 1.8258;
        const double metabolicPerMet = 58.2;
        const double stefanBoltzmann = 5.6697e-8;
        const double neutralSkinCelsius = 33.7;
        const double neutralCoreCelsius = 36.8;
        const double neutralBodyCelsius = 36.49;
        const double neutralSkinBloodFlow = 6.3;

        var skinCelsius = neutralSkinCelsius;
        var coreCelsius = neutralCoreCelsius;
        var skinBloodFlow = neutralSkinBloodFlow;
        var skinShare = 0.1;
        var skinEvaporation = 0.1 * metabolicRateMet;

        var vapourTorr = relativeHumidityPercent * SaturationTorr(airCelsius) / 100;
        var atmospheres = pressureKilopascals * 0.009869;
        var clothingResistance = SquareMetreKelvinPerWattPerClo * clothingClo;
        var clothingAreaFactor = 1.0 + 0.15 * clothingClo;
        var lewisRatio = 2.2 / atmospheres;
        var restingMetabolic = metabolicRateMet * metabolicPerMet;
        var metabolic = restingMetabolic;
        var (criticalWettedness, clothingPermeability) = clothingClo <= 0
            ? (0.38 * Math.Pow(airSpeedMetresPerSecond, -0.29), 1.0)
            : (0.59 * Math.Pow(airSpeedMetresPerSecond, -0.08), 0.45);
        var convection = Math.Max(
            Math.Max(3.0 * Math.Pow(atmospheres, 0.53), 8.600001 * Math.Pow(airSpeedMetresPerSecond * atmospheres, 0.53)), activityConvection);

        var radiation = 4.7;
        var combined = radiation + convection;
        var airResistance = 1.0 / (clothingAreaFactor * combined);
        var operativeCelsius = (radiation * meanRadiantCelsius + convection * airCelsius) / combined;
        var clothingCelsius = operativeCelsius + (skinCelsius - operativeCelsius) / (combined * (airResistance + clothingResistance));
        var airEvaporativeResistance = 1.0 / (lewisRatio * clothingAreaFactor * convection);
        var clothingEvaporativeResistance = clothingResistance / (lewisRatio * clothingPermeability);
        var wettedness = 0.0;
        var dryLoss = 0.0;

        for (var minute = 1; minute <= 60; minute++)
        {
            double previousClothingCelsius;
            do
            {
                previousClothingCelsius = clothingCelsius;
                radiation = 4.0 * stefanBoltzmann * Math.Pow((clothingCelsius + meanRadiantCelsius) / 2.0 + 273.15, 3.0) * 0.72;
                combined = radiation + convection;
                airResistance = 1.0 / (clothingAreaFactor * combined);
                operativeCelsius = (radiation * meanRadiantCelsius + convection * airCelsius) / combined;
                clothingCelsius = (airResistance * skinCelsius + clothingResistance * operativeCelsius) / (airResistance + clothingResistance);
            } while (Math.Abs(clothingCelsius - previousClothingCelsius) > 0.01);

            dryLoss = (skinCelsius - operativeCelsius) / (airResistance + clothingResistance);
            var coreToSkin = (coreCelsius - skinCelsius) * (5.28 + 1.163 * skinBloodFlow);
            var latentRespiration = 0.0023 * metabolic * (44.0 - vapourTorr);
            var dryRespiration = 0.0014 * metabolic * (34.0 - airCelsius);
            var coreStorage = metabolic - coreToSkin - latentRespiration - dryRespiration;
            var skinStorage = coreToSkin - dryLoss - skinEvaporation;
            skinCelsius += skinStorage * bodySquareMetres / (0.97 * skinShare * bodyKilograms * 60.0);
            coreCelsius += coreStorage * bodySquareMetres / (0.97 * (1 - skinShare) * bodyKilograms * 60.0);
            var bodyCelsius = skinShare * skinCelsius + (1 - skinShare) * coreCelsius;

            var skinSignal = skinCelsius - neutralSkinCelsius;
            var warmSkin = Math.Max(skinSignal, 0);
            var coldSkin = Math.Max(-skinSignal, 0);
            var coreSignal = coreCelsius - neutralCoreCelsius;
            var warmCore = Math.Max(coreSignal, 0);
            var coldCore = Math.Max(-coreSignal, 0);
            var warmBody = Math.Max(bodyCelsius - neutralBodyCelsius, 0);

            skinBloodFlow = Math.Clamp((neutralSkinBloodFlow + 120 * warmCore) / (1 + 0.5 * coldSkin), 0.5, 90.0);
            var regulatorySweat = Math.Min(170 * warmBody * Math.Exp(warmSkin / 10.7), 500.0);
            var sweatEvaporation = 0.68 * regulatorySweat;
            var maximumEvaporation =(SaturationTorr(skinCelsius) - vapourTorr) / (airEvaporativeResistance + clothingEvaporativeResistance);
            var sweatShare = sweatEvaporation / maximumEvaporation;
            wettedness = 0.06 + 0.94 * sweatShare;
            var diffusion = wettedness * maximumEvaporation - sweatEvaporation;
            if (wettedness > criticalWettedness)
            {
                wettedness = criticalWettedness;
                sweatShare = criticalWettedness / 0.94;
                sweatEvaporation = sweatShare * maximumEvaporation;
                diffusion = 0.06 * (1.0 - sweatShare) * maximumEvaporation;
            }
            if (maximumEvaporation < 0)
            {
                diffusion = 0;
                sweatEvaporation = 0;
                wettedness = criticalWettedness;
            }
            skinEvaporation = sweatEvaporation + diffusion;
            metabolic = restingMetabolic + 19.4 * coldSkin * coldCore;
            skinShare = 0.0417737 + 0.7451833 / (skinBloodFlow + 0.585417);
        }

        var skinLoss = dryLoss + skinEvaporation;
        var standardConvection = Math.Max(activityConvection, 3.0);
        var standardCombined = standardConvection + radiation;
        var standardClo = 1.52 / (metabolicRateMet + 0.6944) - 0.1835;
        var standardClothingResistance = SquareMetreKelvinPerWattPerClo * standardClo;
        var standardAreaFactor = 1.0 + 0.25 * standardClo;
        var standardClothingEfficiency = 1.0 / (1.0 + 0.155 * standardAreaFactor * standardCombined * standardClo);
        var standardPermeability = 0.45 * standardConvection / standardCombined * (1 - standardClothingEfficiency)
                                   / (standardConvection / standardCombined - standardClothingEfficiency * 0.45);
        var standardDryConductance = 1.0 / (1.0 / (standardAreaFactor * standardCombined) + standardClothingResistance);
        var standardEvaporativeConductance = 1.0 / (1.0 / (lewisRatio * standardAreaFactor * standardConvection)
                                                    + standardClothingResistance / (lewisRatio * standardPermeability));
        var skinSaturationTorr = SaturationTorr(skinCelsius);

        double Imbalance(double standardCelsius) => skinLoss - standardDryConductance * (skinCelsius - standardCelsius)
            - wettedness * standardEvaporativeConductance * (skinSaturationTorr - 0.5 * SaturationTorr(standardCelsius));

        const double probe = 0.0001;
        var estimate = skinCelsius - skinLoss / standardDryConductance;
        var change = 100.0;
        while (Math.Abs(change) > 0.01)
        {
            var here = Imbalance(estimate);
            var next = estimate - probe * here / (Imbalance(estimate + probe) - here);
            change = next - estimate;
            estimate = next;
        }
        return estimate;
    }

    public static double CoolingEffectCelsius(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo, double pressureKilopascals)
    {
        var moving = StandardEffectiveTemperatureCelsius(
            airCelsius, meanRadiantCelsius, relativeHumidityPercent, airSpeedMetresPerSecond, metabolicRateMet, clothingClo, pressureKilopascals,
            activityConvection: 0);
        double StillAirAbove(double cooling) => StandardEffectiveTemperatureCelsius(
            airCelsius - cooling, meanRadiantCelsius - cooling, relativeHumidityPercent, StillAirMetresPerSecond, metabolicRateMet, clothingClo,
            pressureKilopascals, activityConvection: 0) - moving;

        if (StillAirAbove(0) <= 0) return 0;
        double cooler = 0, warmer = LargestCoolingEffectCelsius;
        while (warmer - cooler > 0.001)
        {
            var middle = (cooler + warmer) / 2;
            if (StillAirAbove(middle) > 0) cooler = middle;
            else warmer = middle;
        }
        return (cooler + warmer) / 2;
    }

    private static double ActivityConvection(double metabolicRateMet) =>
        metabolicRateMet > 0.85 ? 5.66 * Math.Pow(metabolicRateMet - 0.85, 0.39) : 0;

    private static double SaturationTorr(double celsius) => Math.Exp(18.6686 - 4030.183 / (celsius + 235.0));
}
