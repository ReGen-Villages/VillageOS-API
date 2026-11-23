namespace vos.Service.Phyllotaxis.Placement;

public sealed record ReservoirAssumptions(
    double CatchmentWeight,
    double LownessWeight,
    double FullScoreSlopePercent,
    double NoScoreSlopePercent,
    double OnBorderFactor,
    double LeastReliefMetres,
    int CandidatesRead,
    double CandidateSpacingMetres,
    int CandidateSpacingCells,
    double GapMetres,
    double ClearOfVillageMetres,
    int PlacesShown,
    double PullInStepShare,
    double PullInLeastShare,
    double ScanLeastStepMetres);

public sealed record CoreAssumptions(
    double WalkingSlopePercent,
    double RampSlopePercent,
    double SteepGroundShare,
    double FlatnessSlopePercent,
    double OnFlowPathPenalty,
    double BelowReservoirMetres,
    double BelowReservoirLeastFactor,
    double FromMiddlePenalty,
    double OnReservoirFactor,
    double InaccessibleFactor,
    double FitExponent);

public sealed record ClearanceAssumptions(double ClearOfVillageMetres, double ClearOfReservoirMetres);

public sealed record TurbineAssumptions(
    double SteepestSlopePercent,
    double SpacingRotorDiameters,
    double SmallestSpacingMetres,
    int PlacesLeast,
    int PlacesMost);

public sealed record SunAssumptions(int FirstHour, int LastHour, double LowestElevationSine, int ShadeSearchCells);

public sealed record SolarFieldAssumptions(
    double PatchSteepestSlopePercent,
    double ClearOfTurbineMetres,
    double FieldRadiusMetres,
    int FieldsMost,
    IReadOnlyList<double> VillageClearanceFactors,
    double FieldGapMetres,
    int WindowMostCells);

public sealed record WastePlantAssumptions(double BearingFromDownwindDegrees, double ClearOfGreenhousesMetres);
