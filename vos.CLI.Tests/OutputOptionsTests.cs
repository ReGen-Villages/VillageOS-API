using Xunit;

namespace vos.CLI.Tests;

public class OutputOptionsTests
{
    [Fact]
    public void Default_ShowGuids_IsFalse()
    {
        var options = OutputOptions.Default;
        Assert.False(options.ShowGuids);
    }

    [Fact]
    public void FormatIdentifier_WithShowGuidsFalse_ReturnsNameOnly()
    {
        var options = new OutputOptions { ShowGuids = false };
        var result = options.FormatIdentifier("TestName", "12345678-1234-1234-1234-123456789012");
        Assert.Equal("TestName", result);
    }

    [Fact]
    public void FormatIdentifier_WithShowGuidsTrue_ReturnsNameAndGuid()
    {
        var options = new OutputOptions { ShowGuids = true };
        var result = options.FormatIdentifier("TestName", "12345678-1234-1234-1234-123456789012");
        Assert.Equal("TestName (12345678-1234-1234-1234-123456789012)", result);
    }

    [Fact]
    public void FormatIdentifier_WithGuidOverload_WorksCorrectly()
    {
        var options = new OutputOptions { ShowGuids = true };
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var result = options.FormatIdentifier("TestName", guid);
        Assert.Equal("TestName (12345678-1234-1234-1234-123456789012)", result);
    }

    [Fact]
    public void FormatIdentifier_WithEmptyName_ReturnsGuidWhenShowGuids()
    {
        var options = new OutputOptions { ShowGuids = true };
        var result = options.FormatIdentifier("", "12345678-1234-1234-1234-123456789012");
        Assert.Equal("12345678-1234-1234-1234-123456789012", result);
    }

    [Fact]
    public void FormatIdentifier_WithNAName_ReturnsGuidWhenShowGuids()
    {
        var options = new OutputOptions { ShowGuids = true };
        var result = options.FormatIdentifier("N/A", "12345678-1234-1234-1234-123456789012");
        Assert.Equal("12345678-1234-1234-1234-123456789012", result);
    }

    [Fact]
    public void ParseFromArgs_WithNoFlag_ReturnsFalse()
    {
        var (options, remaining) = OutputOptions.ParseFromArgs("things");
        Assert.False(options.ShowGuids);
        Assert.Equal("things", remaining);
    }

    [Fact]
    public void ParseFromArgs_WithShowGuidsFlag_ReturnsTrue()
    {
        var (options, remaining) = OutputOptions.ParseFromArgs("things --showguids");
        Assert.True(options.ShowGuids);
        Assert.Equal("things", remaining);
    }

    [Fact]
    public void ParseFromArgs_WithShortFlag_ReturnsTrue()
    {
        var (options, remaining) = OutputOptions.ParseFromArgs("things -g");
        Assert.True(options.ShowGuids);
        Assert.Equal("things", remaining);
    }

    [Fact]
    public void ParseFromArgs_WithFlagAtBeginning_ReturnsTrue()
    {
        var (options, remaining) = OutputOptions.ParseFromArgs("--showguids things");
        Assert.True(options.ShowGuids);
        Assert.Equal("things", remaining);
    }

    [Fact]
    public void ParseFromArgs_WithFlagInMiddle_ReturnsTrue()
    {
        var (options, remaining) = OutputOptions.ParseFromArgs("thing --showguids TestThing");
        Assert.True(options.ShowGuids);
        Assert.Equal("thing TestThing", remaining);
    }

    [Fact]
    public void ParseFromArgs_CaseInsensitive_ReturnsTrue()
    {
        var (options, remaining) = OutputOptions.ParseFromArgs("things --SHOWGUIDS");
        Assert.True(options.ShowGuids);
        Assert.Equal("things", remaining);
    }

    [Fact]
    public void ParseFromArgs_WithEmptyString_ReturnsFalse()
    {
        var (options, remaining) = OutputOptions.ParseFromArgs("");
        Assert.False(options.ShowGuids);
        Assert.Equal("", remaining);
    }

    [Fact]
    public void ParseFromArgs_WithNullString_ReturnsFalse()
    {
        var (options, remaining) = OutputOptions.ParseFromArgs(null!);
        Assert.False(options.ShowGuids);
        Assert.Null(remaining);
    }
}
