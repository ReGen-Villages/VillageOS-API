using FluentAssertions;
using vos.Microservice.Shared.Contracts.Validation;
using Xunit;

namespace vos.Microservice.Shared.Contracts.Tests;

public class ContractValidationExceptionTests
{
    [Fact]
    public void Message_PassingResult_IndicatesMisuse()
    {
        var ex = new ContractValidationException("schema-id", ContractValidationResult.Success);
        ex.Result.IsValid.Should().BeTrue();
        ex.Message.Should().Contain("passing result");
        ex.Message.Should().Contain("schema-id");
    }

    [Fact]
    public void Message_FailureWithNoErrors_IncludesSchemaId()
    {
        var emptyFailure = ContractValidationResult.Failure(Array.Empty<ContractValidationError>());
        var ex = new ContractValidationException("schema-id", emptyFailure);
        ex.Result.IsValid.Should().BeFalse();
        ex.Message.Should().Contain("schema-id");
    }

    [Fact]
    public void Message_FailureWithSingleError_QuotesPathAndCode()
    {
        var failure = ContractValidationResult.Failure(new[]
        {
            new ContractValidationError("#/handlerId", "Format", "UuidExpected: #/handlerId")
        });

        var ex = new ContractValidationException("schema-id", failure);

        ex.Message.Should().Contain("#/handlerId");
        ex.Message.Should().Contain("Format");
        ex.Message.Should().NotContain("more");
    }

    [Fact]
    public void Message_FailureWithMultipleErrors_AppendsMoreSuffix()
    {
        var failure = ContractValidationResult.Failure(new[]
        {
            new ContractValidationError("#/a", "Required", "First"),
            new ContractValidationError("#/b", "Required", "Second"),
            new ContractValidationError("#/c", "Required", "Third")
        });

        var ex = new ContractValidationException("schema-id", failure);

        ex.Message.Should().Contain("#/a");
        ex.Message.Should().Contain("(+2 more)");
    }
}
