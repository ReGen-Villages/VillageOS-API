using System.Text.Json;
using vos.Taproot;
using Xunit;

namespace vos.Taproot.Tests;

public class JsonElementExtensionsTests
{
    #region GetStringOrDefault Tests

    [Fact]
    public void GetStringOrDefault_WhenPropertyExists_ReturnsValue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Name\":\"TestValue\"}");

        var result = json.GetStringOrDefault("Name");

        Assert.Equal("TestValue", result);
    }

    [Fact]
    public void GetStringOrDefault_WhenPropertyMissing_ReturnsDefault()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = json.GetStringOrDefault("Name");

        Assert.Equal("N/A", result);
    }

    [Fact]
    public void GetStringOrDefault_WhenPropertyNull_ReturnsDefault()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Name\":null}");

        var result = json.GetStringOrDefault("Name");

        Assert.Equal("N/A", result);
    }

    [Fact]
    public void GetStringOrDefault_WithCustomDefault_ReturnsCustomDefault()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = json.GetStringOrDefault("Name", "unknown");

        Assert.Equal("unknown", result);
    }

    #endregion

    #region GetBoolOrDefault Tests

    [Fact]
    public void GetBoolOrDefault_WhenTrue_ReturnsTrue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"IsRunning\":true}");

        var result = json.GetBoolOrDefault("IsRunning");

        Assert.True(result);
    }

    [Fact]
    public void GetBoolOrDefault_WhenFalse_ReturnsFalse()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"IsRunning\":false}");

        var result = json.GetBoolOrDefault("IsRunning");

        Assert.False(result);
    }

    [Fact]
    public void GetBoolOrDefault_WhenMissing_ReturnsFalse()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = json.GetBoolOrDefault("IsRunning");

        Assert.False(result);
    }

    #endregion

    #region GetIntOrDefault Tests

    [Fact]
    public void GetIntOrDefault_WhenPropertyExists_ReturnsValue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Count\":42}");

        var result = json.GetIntOrDefault("Count");

        Assert.Equal(42, result);
    }

    [Fact]
    public void GetIntOrDefault_WhenPropertyMissing_ReturnsDefault()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = json.GetIntOrDefault("Count");

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetIntOrDefault_WhenPropertyNull_ReturnsDefault()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Count\":null}");

        var result = json.GetIntOrDefault("Count", 5);

        Assert.Equal(5, result);
    }

    #endregion

    #region GetNullableInt Tests

    [Fact]
    public void GetNullableInt_WhenPropertyExists_ReturnsValue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"ProcessId\":12345}");

        var result = json.GetNullableInt("ProcessId");

        Assert.Equal(12345, result);
    }

    [Fact]
    public void GetNullableInt_WhenPropertyMissing_ReturnsNull()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = json.GetNullableInt("ProcessId");

        Assert.Null(result);
    }

    [Fact]
    public void GetNullableInt_WhenPropertyNull_ReturnsNull()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"ProcessId\":null}");

        var result = json.GetNullableInt("ProcessId");

        Assert.Null(result);
    }

    #endregion

    #region GetNullableDateTime Tests

    [Fact]
    public void GetNullableDateTime_WhenPropertyExists_ReturnsValue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Timestamp\":\"2025-01-30T10:00:00Z\"}");

        var result = json.GetNullableDateTime("Timestamp");

        Assert.NotNull(result);
        Assert.Equal(2025, result.Value.Year);
    }

    [Fact]
    public void GetNullableDateTime_WhenPropertyMissing_ReturnsNull()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = json.GetNullableDateTime("Timestamp");

        Assert.Null(result);
    }

    [Fact]
    public void GetNullableDateTime_WhenPropertyNull_ReturnsNull()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Timestamp\":null}");

        var result = json.GetNullableDateTime("Timestamp");

        Assert.Null(result);
    }

    #endregion

    #region HasObjectProperty Tests

    [Fact]
    public void HasObjectProperty_WhenObjectExists_ReturnsTrue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Properties\":{\"key\":\"value\"}}");

        var result = json.HasObjectProperty("Properties");

        Assert.True(result);
    }

    [Fact]
    public void HasObjectProperty_WhenPropertyMissing_ReturnsFalse()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = json.HasObjectProperty("Properties");

        Assert.False(result);
    }

    [Fact]
    public void HasObjectProperty_WhenPropertyNotObject_ReturnsFalse()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Properties\":\"string\"}");

        var result = json.HasObjectProperty("Properties");

        Assert.False(result);
    }

    [Fact]
    public void HasObjectProperty_WhenPropertyIsArray_ReturnsFalse()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Properties\":[1,2,3]}");

        var result = json.HasObjectProperty("Properties");

        Assert.False(result);
    }

    #endregion

    #region FormatPropertyValue Tests

    [Fact]
    public void FormatPropertyValue_WhenString_ReturnsString()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("\"hello\"");

        var result = json.FormatPropertyValue();

        Assert.Equal("hello", result);
    }

    [Fact]
    public void FormatPropertyValue_WhenTrue_ReturnsTrue()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("true");

        var result = json.FormatPropertyValue();

        Assert.Equal("true", result);
    }

    [Fact]
    public void FormatPropertyValue_WhenFalse_ReturnsFalse()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("false");

        var result = json.FormatPropertyValue();

        Assert.Equal("false", result);
    }

    [Fact]
    public void FormatPropertyValue_WhenNull_ReturnsNull()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("null");

        var result = json.FormatPropertyValue();

        Assert.Equal("null", result);
    }

    [Fact]
    public void FormatPropertyValue_WhenNumber_ReturnsRawText()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("42");

        var result = json.FormatPropertyValue();

        Assert.Equal("42", result);
    }

    [Fact]
    public void FormatPropertyValue_WhenObject_ReturnsRawText()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"key\":\"value\"}");

        var result = json.FormatPropertyValue();

        Assert.Contains("key", result);
        Assert.Contains("value", result);
    }

    [Fact]
    public void FormatPropertyValue_WhenArray_ReturnsRawText()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("[1,2,3]");

        var result = json.FormatPropertyValue();

        Assert.Equal("[1,2,3]", result);
    }

    [Fact]
    public void FormatPropertyValue_WhenEmptyString_ReturnsEmpty()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("\"\"");

        var result = json.FormatPropertyValue();

        Assert.Equal("", result);
    }

    #endregion

    #region GetPropertyOrNull Tests

    [Fact]
    public void GetPropertyOrNull_WhenPropertyExists_ReturnsElement()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"Child\":{\"Value\":123}}");

        var result = json.GetPropertyOrNull("Child");

        Assert.NotNull(result);
        Assert.Equal(123, result.Value.GetProperty("Value").GetInt32());
    }

    [Fact]
    public void GetPropertyOrNull_WhenPropertyMissing_ReturnsNull()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = json.GetPropertyOrNull("Child");

        Assert.Null(result);
    }

    #endregion
}
