using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using vos.Service.Shared.Contracts.Validation;
using vos.Service.Shared.Middleware;
using Xunit;

namespace vos.Service.Shared.Tests.Middleware;

public class ContractValidationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddContractValidation_RegistersSchemaRegistryAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddContractValidation();

        var sp = services.BuildServiceProvider();
        var a = sp.GetService<SchemaRegistry>();
        var b = sp.GetService<SchemaRegistry>();

        a.Should().NotBeNull();
        b.Should().BeSameAs(a, "SchemaRegistry must be a singleton (eager schema load is per-process)");
    }

    [Fact]
    public void AddContractValidation_RegistersSchemaValidatorAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddContractValidation();

        var sp = services.BuildServiceProvider();
        var a = sp.GetService<SchemaValidator>();
        var b = sp.GetService<SchemaValidator>();

        a.Should().NotBeNull();
        b.Should().BeSameAs(a);
    }
}
