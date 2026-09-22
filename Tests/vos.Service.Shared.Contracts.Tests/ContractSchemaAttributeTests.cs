using FluentAssertions;
using vos.Service.Shared.Contracts.Validation;
using Xunit;

namespace vos.Service.Shared.Contracts.Tests;

public class ContractSchemaAttributeTests
{
    [Fact]
    public void Ctor_ValidId_SetsIdProperty()
    {
        var attr = new ContractSchemaAttribute("https://villageos/contracts/example.schema.json");
        attr.Id.Should().Be("https://villageos/contracts/example.schema.json");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Ctor_NullOrWhitespaceId_Throws(string? id)
    {
        Action act = () => _ = new ContractSchemaAttribute(id!);
        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }
}
