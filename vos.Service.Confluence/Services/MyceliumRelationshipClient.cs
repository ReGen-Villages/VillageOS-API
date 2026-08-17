using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Confluence.Services;

// The two writes starting an analysis needs. Separate from the fetcher, which speaks to endpoint
// services rather than to the model.
public sealed class MyceliumRelationshipClient : MyceliumClientBase
{
    public MyceliumRelationshipClient(
        IHttpClientFactory httpClientFactory, ILogger<MyceliumRelationshipClient> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken) { }

    public async Task<Guid?> FindPredicateAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10));
            var response = await client.GetAsync(
                $"{MyceliumUrl}/api/things?name={Uri.EscapeDataString(name)}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            // A name that matches nothing answers `null`, not an object. Enumerating that throws, and
            // the catch below would log a missing predicate as an error the operator cannot act on.
            var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (root.ValueKind != JsonValueKind.Object) return null;

            foreach (var property in root.EnumerateObject())
                if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(property.Value.GetString(), out var id))
                    return id;

            return null;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error finding the '{Name}' predicate", name);
            return null;
        }
    }

    public async Task<bool> CreateRelationshipAsync(
        Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
            var response = await client.PostAsJsonAsync(
                $"{MyceliumUrl}/api/relationships",
                new { subjectId, predicateId, targetId },
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Error relating {Subject} to {Target}", subjectId, targetId);
            return false;
        }
    }
}
