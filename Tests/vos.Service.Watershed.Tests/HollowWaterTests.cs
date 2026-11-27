using FluentAssertions;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

// Each cell is one square metre, so a volume in cubic metres is a sum of depths in metres.
public class HollowWaterTests
{
    private const double Spill = 2.0;

    private static readonly double[] TwoPits = [0.0, 0.5, 1.0, 0.2, 0.6];
    private static readonly int[] TwoPitBasins = [0, 0, 0, 1, 1];
    private static readonly HollowRidge[] OneRidge = [new(0, 1, 1.0)];

    private static readonly double[] ThreePits = [0.4, 0.8, 0.1, 0.5, 0.0, 0.3];
    private static readonly int[] ThreePitBasins = [0, 0, 1, 1, 2, 2];
    private static readonly HollowRidge[] TwoRidges = [new(0, 1, 1.0), new(1, 2, 1.5)];

    private static HollowFill TwoPitsGiven(double intoTheFirst) =>
        HollowWater.Fill(TwoPits, TwoPitBasins, OneRidge, [intoTheFirst, 0], Spill, 1);

    private static void ShouldStand(HollowFill fill, params double[] depths) =>
        fill.StandingDepths.Should().Equal(depths, (actual, expected) => Math.Abs(actual - expected) < 1e-9);

    // (L − 0) + (L − 0.5) = 1 → L = 0.75 < ridge 1.
    [Fact]
    public void Water_gathered_into_one_pit_stays_there_and_the_pit_across_the_ridge_stays_dry()
    {
        var fill = TwoPitsGiven(1);

        ShouldStand(fill, 0.75, 0.25, 0, 0, 0);
        fill.Overflow.Should().Be(0);
    }

    // First pit to ridge 1: 1 + 0.5 + 0 = 1.5; 2 − 1.5 = 0.5 crosses: (L − 0.2) + (L − 0.6) = 0.5 → L = 0.65.
    [Fact]
    public void A_pit_full_to_its_ridge_spills_into_the_pit_beside_it()
    {
        ShouldStand(TwoPitsGiven(2), 1.0, 0.5, 0, 0.45, 0.05);
    }

    // Both pits to ridge 1: 1.5 + 1.2 = 2.7 < 4, so one level over all five cells: 5L − 2.3 = 4 → L = 1.26.
    [Fact]
    public void Once_both_pits_are_full_to_the_ridge_they_share_one_level()
    {
        ShouldStand(TwoPitsGiven(4), 1.26, 0.76, 0.26, 1.06, 0.66);
    }

    // Store to spill 2: 5 × 2 − 2.3 = 7.7; overflow 10 − 7.7 = 2.3.
    [Fact]
    public void Water_past_the_hollow_s_store_fills_it_to_its_spill_level_and_overflows()
    {
        var fill = TwoPitsGiven(10);

        ShouldStand(fill, 2.0, 1.5, 1.0, 1.8, 1.4);
        fill.Overflow.Should().BeApproximately(2.3, 1e-9);
    }

    // First pit to ridge 1: 0.6 + 0.2 = 0.8; 1.5 − 0.8 = 0.7 into the second: (L − 0.1) + (L − 0.5) = 0.7 → L = 0.65.
    [Fact]
    public void Water_spilling_from_the_highest_pit_fills_the_next_before_the_third()
    {
        ShouldStand(HollowWater.Fill(ThreePits, ThreePitBasins, TwoRidges, [1.5, 0, 0], Spill, 1), 0.6, 0.2, 0.55, 0.15, 0, 0);
    }

    // First two to ridge 1: 0.8 + 1.4 = 2.2; to ridge 1.5: + 4 × 0.5 = 4.2; 4.6 − 4.2 = 0.4 into the third:
    // L + (L − 0.3) = 0.4 → L = 0.35.
    [Fact]
    public void Water_reaches_the_third_pit_only_once_the_first_two_are_full_to_the_higher_ridge()
    {
        ShouldStand(HollowWater.Fill(ThreePits, ThreePitBasins, TwoRidges, [4.6, 0, 0], Spill, 1), 1.1, 0.7, 1.4, 1.0, 0.35, 0.05);
    }
}
