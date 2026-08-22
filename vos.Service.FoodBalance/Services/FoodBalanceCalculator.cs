namespace vos.Service.FoodBalance.Services;

public sealed record FoodBalanceInputs(
    double ProductiveFootprintHectares,
    double PeopleFedPerHectarePerYear,
    double PopulationResidents);

public sealed record FoodBalanceOutputs(double PeopleFed, double PctOfPopulationFed);

// How many residents the land given to growing can feed, and what share of the population that is.
//
// The arithmetic is two lines, and it is a service anyway so the figure carries where it came from: the
// yield assumption it used, and the productive area land allocation worked out. A number produced in a
// page carries neither, and cannot move when a planner corrects the assumption.
//
// Yield as people-fed-per-hectare hides crop mix, climate and diet. It is an intake-stage estimate and
// belongs beside that caveat wherever it is shown.
//
// Neither output is rounded. People fed is conceptually a whole number, but a figure rounded here no
// longer agrees with the percentage worked out from it, so rounding belongs where the two are displayed.
public static class FoodBalanceCalculator
{
    public static FoodBalanceOutputs Compute(FoodBalanceInputs input)
    {
        var peopleFed = input.ProductiveFootprintHectares * input.PeopleFedPerHectarePerYear;

        // A site with no residents has nobody to feed, which is a share of nothing rather than a division.
        var pctOfPopulationFed = input.PopulationResidents > 0
            ? peopleFed / input.PopulationResidents * 100.0
            : 0.0;

        return new FoodBalanceOutputs(peopleFed, pctOfPopulationFed);
    }
}
