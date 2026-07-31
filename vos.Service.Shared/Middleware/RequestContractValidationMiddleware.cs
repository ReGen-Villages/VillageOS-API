using System.Text.Json;
using Microsoft.AspNetCore.Http;
using vos.Service.Shared.Contracts.Validation;

namespace vos.Service.Shared.Middleware;

public sealed class RequestContractValidationMiddleware
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly SchemaRegistry _registry;
    private readonly SchemaValidator _validator;

    public RequestContractValidationMiddleware(RequestDelegate next, SchemaRegistry registry, SchemaValidator validator)
    {
        _next = next;
        _registry = registry;
        _validator = validator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var meta = context.GetEndpoint()?.Metadata.GetMetadata<ContractValidationMetadata>();
        if (meta is null)
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync();
        }
        context.Request.Body.Position = 0;

        try
        {
            using var _ = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            await WriteFailureAsync(context, meta.SchemaId, new[]
            {
                new ContractValidationError(null, "Malformed", $"Request body is not valid JSON: {ex.Message}")
            });
            return;
        }

        var schema = _registry.Get(meta.SchemaId);
        var result = _validator.Validate(body, schema);

        if (result.IsValid)
        {
            await _next(context);
            return;
        }

        await WriteFailureAsync(context, meta.SchemaId, result.Errors);
    }

    private static Task WriteFailureAsync(HttpContext context, string schemaId, IEnumerable<ContractValidationError> errors)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, new { schemaId, errors }, ResponseJsonOptions);
    }
}
