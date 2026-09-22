using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Service.Shared.Contracts.Validation;

namespace vos.Service.Shared.Middleware;

public static class ContractValidationServiceCollectionExtensions
{
    public static IServiceCollection AddContractValidation(this IServiceCollection services)
    {
        services.TryAddSingleton<SchemaRegistry>();
        services.TryAddSingleton<SchemaValidator>();
        return services;
    }
}

public static class ContractValidationApplicationBuilderExtensions
{
    public static IApplicationBuilder UseRequestContractValidation(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestContractValidationMiddleware>();
}

public static class ContractValidationEndpointExtensions
{
    public static IEndpointConventionBuilder RequireContract<T>(this IEndpointConventionBuilder builder)
    {
        var attr = typeof(T).GetCustomAttribute<ContractSchemaAttribute>()
            ?? throw new InvalidOperationException(
                $"Type '{typeof(T).FullName}' is missing [ContractSchema(\"<id>\")] — RequireContract<T>() needs the schema $id resolvable from the DTO.");
        return builder.WithMetadata(new ContractValidationMetadata(attr.Id));
    }
}
