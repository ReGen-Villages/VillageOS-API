using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using vos.ManagedMicroservice.Shared.Contracts.Validation;
using vos.ManagedMicroservice.Shared.Middleware;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Tests.Middleware;

public class RequestContractValidationMiddlewareTests
{
    private const string MetabolismSchemaId = "https://villageos/contracts/handle-request-metabolism.schema.json";

    private static readonly SchemaRegistry Registry = new();
    private static readonly SchemaValidator Validator = new();

    [Fact]
    public async Task InvokeAsync_NoEndpoint_CallsNext()
    {
        var ctx = NewContext(body: "{}");
        var (next, called) = CapturingNext();
        var sut = new RequestContractValidationMiddleware(next, Registry, Validator);

        await sut.InvokeAsync(ctx);

        called().Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_EndpointWithoutContractMetadata_CallsNext()
    {
        var ctx = NewContext(body: "{}", endpointMetadata: Array.Empty<object>());
        var (next, called) = CapturingNext();
        var sut = new RequestContractValidationMiddleware(next, Registry, Validator);

        await sut.InvokeAsync(ctx);

        called().Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_ValidBody_CallsNextAndLeavesBodyReadable()
    {
        var validJson = "{\"subjectId\":\"thing-a\",\"targetId\":\"thing-b\"}";
        var ctx = NewContext(body: validJson, endpointMetadata: new object[]
        {
            new ContractValidationMetadata(MetabolismSchemaId)
        });

        string? bodySeenByNext = null;
        RequestDelegate next = async http =>
        {
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
            bodySeenByNext = await reader.ReadToEndAsync();
        };

        var sut = new RequestContractValidationMiddleware(next, Registry, Validator);

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        bodySeenByNext.Should().Be(validJson, "the middleware must rewind the body stream for downstream handlers");
    }

    [Fact]
    public async Task InvokeAsync_InvalidBody_Writes400AndSkipsNext()
    {
        var missingRequired = "{\"subjectId\":\"thing-a\"}"; // targetId missing
        var ctx = NewContext(body: missingRequired, endpointMetadata: new object[]
        {
            new ContractValidationMetadata(MetabolismSchemaId)
        });

        var (next, called) = CapturingNext();
        var sut = new RequestContractValidationMiddleware(next, Registry, Validator);

        await sut.InvokeAsync(ctx);

        called().Should().BeFalse("downstream handler must not see malformed input");
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.ContentType.Should().Contain("application/json");
    }

    [Fact]
    public async Task InvokeAsync_FailureResponse_PayloadHasSchemaIdAndErrors()
    {
        var bodyWithUnknownProp = "{\"subjectId\":\"thing-a\",\"targetId\":\"thing-b\",\"foo\":\"bar\"}";
        var ctx = NewContext(body: bodyWithUnknownProp, endpointMetadata: new object[]
        {
            new ContractValidationMetadata(MetabolismSchemaId)
        });

        var (next, _) = CapturingNext();
        var sut = new RequestContractValidationMiddleware(next, Registry, Validator);

        await sut.InvokeAsync(ctx);

        var payload = ReadResponseAs<FailurePayload>(ctx);
        payload.Should().NotBeNull();
        payload!.SchemaId.Should().Be(MetabolismSchemaId);
        payload.Errors.Should().NotBeNullOrEmpty();
        payload.Errors.Should().Contain(e => e.Code == "AdditionalProperties");
    }

    [Fact]
    public async Task InvokeAsync_MalformedJson_Writes400AndSkipsNext()
    {
        var malformed = "{ not json at all";
        var ctx = NewContext(body: malformed, endpointMetadata: new object[]
        {
            new ContractValidationMetadata(MetabolismSchemaId)
        });

        var (next, called) = CapturingNext();
        var sut = new RequestContractValidationMiddleware(next, Registry, Validator);

        await sut.InvokeAsync(ctx);

        called().Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    // --- helpers ---

    private static HttpContext NewContext(string body, object[]? endpointMetadata = null)
    {
        var ctx = new DefaultHttpContext
        {
            Request =
            {
                Method = "POST",
                ContentType = "application/json",
                Body = new MemoryStream(Encoding.UTF8.GetBytes(body))
            },
            Response = { Body = new MemoryStream() }
        };
        if (endpointMetadata is not null)
        {
            var endpoint = new Endpoint(
                requestDelegate: null,
                metadata: new EndpointMetadataCollection(endpointMetadata),
                displayName: "test");
            ctx.SetEndpoint(endpoint);
        }
        return ctx;
    }

    private static (RequestDelegate next, Func<bool> wasCalled) CapturingNext()
    {
        var flag = false;
        RequestDelegate next = _ =>
        {
            flag = true;
            return Task.CompletedTask;
        };
        return (next, () => flag);
    }

    private static T? ReadResponseAs<T>(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return JsonSerializer.Deserialize<T>(ctx.Response.Body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private sealed record FailurePayload(string SchemaId, ContractValidationError[] Errors);
}
