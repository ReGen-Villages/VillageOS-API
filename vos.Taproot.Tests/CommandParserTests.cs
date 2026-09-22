using Xunit;

namespace vos.Taproot.Tests;

public class CommandParserTests
{
    [Fact]
    public void TryParseSubcommand_WithValidInput_ReturnsTrue()
    {
        var result = CommandParser.TryParseSubcommand("create thing1", out var subcommand, out var args);

        Assert.True(result);
        Assert.Equal("create", subcommand);
        Assert.Single(args);
        Assert.Equal("thing1", args[0]);
    }

    [Fact]
    public void TryParseSubcommand_WithMultipleArgs_ParsesAllArgs()
    {
        var result = CommandParser.TryParseSubcommand("history thing1 prop1 start end", out var subcommand, out var args);

        Assert.True(result);
        Assert.Equal("history", subcommand);
        Assert.Equal(4, args.Length);
        Assert.Equal(new[] { "thing1", "prop1", "start", "end" }, args);
    }

    [Fact]
    public void TryParseSubcommand_WithSingleWord_ReturnsSubcommandOnly()
    {
        var result = CommandParser.TryParseSubcommand("help", out var subcommand, out var args);

        Assert.True(result);
        Assert.Equal("help", subcommand);
        Assert.Empty(args);
    }

    [Fact]
    public void TryParseSubcommand_WithEmptyInput_ReturnsFalse()
    {
        var result = CommandParser.TryParseSubcommand("", out var subcommand, out var args);

        Assert.False(result);
        Assert.Equal("", subcommand);
        Assert.Empty(args);
    }

    [Fact]
    public void TryParseSubcommand_WithWhitespaceOnly_ReturnsFalse()
    {
        var result = CommandParser.TryParseSubcommand("   ", out var subcommand, out var args);

        Assert.False(result);
        Assert.Equal("", subcommand);
        Assert.Empty(args);
    }

    [Fact]
    public void TryParseSubcommand_WithExtraSpaces_IgnoresExtraSpaces()
    {
        var result = CommandParser.TryParseSubcommand("  create   thing1   prop1  ", out var subcommand, out var args);

        Assert.True(result);
        Assert.Equal("create", subcommand);
        Assert.Equal(2, args.Length);
        Assert.Equal(new[] { "thing1", "prop1" }, args);
    }
}
