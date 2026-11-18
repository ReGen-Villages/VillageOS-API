using System.Text.Json;
using vos.Tests.Shared;
using Xunit;

namespace vos.ContinuousIntegration.Tests;

public class TestSeedsAreWhereTheHelperSaysTests
{
    [Fact]
    public void Each_named_seed_is_a_file_in_the_folder_and_All_lists_exactly_those()
    {
        string[] named = [TestSeeds.Catchments, TestSeeds.CatchmentsWithAnUnreadablePage];

        Assert.All(named, seed => Assert.True(File.Exists(seed), $"{seed} is missing"));
        Assert.Equal(named.Order(), TestSeeds.All);
    }

    [Fact]
    public void Each_seed_reads_as_a_model_with_Things_and_Relationships()
    {
        Assert.All(TestSeeds.All, seed =>
        {
            using var model = JsonDocument.Parse(File.ReadAllText(seed));
            Assert.NotEqual(0, model.RootElement.GetProperty("Things").GetArrayLength());
            Assert.NotEqual(0, model.RootElement.GetProperty("Relationships").GetArrayLength());
        });
    }
}
