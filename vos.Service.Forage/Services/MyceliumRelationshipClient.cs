using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Shared;

namespace vos.Service.Forage.Services;

// The one write starting an analysis needs. Separate from the fetcher, which speaks to endpoint
// services rather than to the model.
public sealed class MyceliumRelationshipClient : MyceliumClientBase
{
    public MyceliumRelationshipClient(
        IHttpClientFactory httpClientFactory, ILogger<MyceliumRelationshipClient> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey) { }

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
