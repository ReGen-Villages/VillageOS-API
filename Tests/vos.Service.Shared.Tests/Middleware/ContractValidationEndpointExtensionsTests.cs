using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using vos.Service.Shared.Contracts.Validation;
using vos.Service.Shared.Middleware;
using Xunit;

namespace vos.Service.Shared.Tests.Middleware;

public class ContractValidationEndpointExtensionsTests
{
    private const string MetabolismSchemaId = "https://villageos/contracts/handle-request-metabolism.schema.json";

    [ContractSchema(MetabolismSchemaId)]
    private sealed record FakeDtoWithAttribute;

    private sealed record FakeDtoWithoutAttribute;

    [Fact]
    public void RequireContract_DtoWithContractSchemaAttribute_AddsMetadataWithSchemaId()
    {
        var capture = new MetadataCapture();

        capture.RequireContract<FakeDtoWithAttribute>();

        capture.Captured.Should().ContainSingle()
            .Which.Should().BeOfType<ContractValidationMetadata>()
            .Which.SchemaId.Should().Be(MetabolismSchemaId);
    }

    [Fact]
    public void RequireContract_DtoWithoutContractSchemaAttribute_Throws()
    {
        var capture = new MetadataCapture();

        var act = () => capture.RequireContract<FakeDtoWithoutAttribute>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FakeDtoWithoutAttribute*ContractSchema*");
    }

    // Minimal IEndpointConventionBuilder that just records added metadata.
    private sealed class MetadataCapture : IEndpointConventionBuilder
    {
        public readonly List<object> Captured = new();
        public void Add(Action<EndpointBuilder> convention)
        {
            var probe = new ProbeEndpointBuilder();
            convention(probe);
            Captured.AddRange(probe.Metadata);
        }

        private sealed class ProbeEndpointBuilder : EndpointBuilder
        {
            public override Endpoint Build() =>
                new(_ => Task.CompletedTask, new EndpointMetadataCollection(Metadata), DisplayName);
        }
    }
}
