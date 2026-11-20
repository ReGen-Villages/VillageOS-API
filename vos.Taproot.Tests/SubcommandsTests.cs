using Xunit;

namespace vos.Taproot.Tests;

public class SubcommandsTests
{
    private readonly List<string> _ran = [];
    private int _usageShown;

    private Task Run(string arg) => Subcommands.RunAsync(arg, new Dictionary<string, Func<string[], Task>>(StringComparer.OrdinalIgnoreCase)
    {
        ["list"] = args =>
        {
            _ran.Add("list " + string.Join(",", args));
            return Task.CompletedTask;
        },
    }, () => _usageShown++);

    [Fact]
    public async Task A_named_subcommand_runs_with_the_words_after_it()
    {
        await Run("list one two");

        Assert.Equal(["list one,two"], _ran);
        Assert.Equal(0, _usageShown);
    }

    [Fact]
    public async Task The_table_decides_whether_case_matters()
    {
        await Run("LIST");

        Assert.Equal(["list "], _ran);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public async Task No_subcommand_or_an_unknown_one_shows_usage_and_runs_nothing(string arg)
    {
        await Run(arg);

        Assert.Empty(_ran);
        Assert.Equal(1, _usageShown);
    }
}
