using FluentAssertions;
using NJsonSchema.Validation;
using vos.ManagedMicroservice.Shared.Contracts.Validation;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Contracts.Tests;

/// <summary>
/// Pins the <see cref="SchemaValidator.NormalizeCode"/> dispatch table. The public surface
/// (<see cref="ContractValidationError.Code"/>) is part of the contract these tests defend.
/// </summary>
public class NormalizeCodeTests
{
    [Theory]
    [InlineData(ValidationErrorKind.PropertyRequired,              "Required")]
    [InlineData(ValidationErrorKind.NoAdditionalPropertiesAllowed, "AdditionalProperties")]
    [InlineData(ValidationErrorKind.TooFewItems,                   "ArrayLength")]
    [InlineData(ValidationErrorKind.TooManyItems,                  "ArrayLength")]
    [InlineData(ValidationErrorKind.UuidExpected,                  "Format")]
    [InlineData(ValidationErrorKind.UriExpected,                   "Format")]
    [InlineData(ValidationErrorKind.DateExpected,                  "Format")]
    [InlineData(ValidationErrorKind.DateTimeExpected,              "Format")]
    [InlineData(ValidationErrorKind.TimeExpected,                  "Format")]
    [InlineData(ValidationErrorKind.TimeSpanExpected,              "Format")]
    [InlineData(ValidationErrorKind.EmailExpected,                 "Format")]
    [InlineData(ValidationErrorKind.IpV4Expected,                  "Format")]
    [InlineData(ValidationErrorKind.IpV6Expected,                  "Format")]
    [InlineData(ValidationErrorKind.HostnameExpected,              "Format")]
    [InlineData(ValidationErrorKind.GuidExpected,                  "Format")]
    [InlineData(ValidationErrorKind.StringExpected,                "Type")]
    [InlineData(ValidationErrorKind.IntegerExpected,               "Type")]
    [InlineData(ValidationErrorKind.NumberExpected,                "Type")]
    [InlineData(ValidationErrorKind.BooleanExpected,               "Type")]
    [InlineData(ValidationErrorKind.ArrayExpected,                 "Type")]
    [InlineData(ValidationErrorKind.ObjectExpected,                "Type")]
    [InlineData(ValidationErrorKind.NullExpected,                  "Type")]
    public void NormalizeCode_RecognisedKind_MapsToPublicFamily(ValidationErrorKind kind, string expected)
    {
        SchemaValidator.NormalizeCode(kind).Should().Be(expected);
    }

    [Theory]
    [InlineData(ValidationErrorKind.NumberTooBig)]
    [InlineData(ValidationErrorKind.NumberTooSmall)]
    [InlineData(ValidationErrorKind.PatternMismatch)]
    public void NormalizeCode_UnrecognisedKind_FallsThroughToEnumName(ValidationErrorKind kind)
    {
        SchemaValidator.NormalizeCode(kind).Should().Be(kind.ToString());
    }
}
