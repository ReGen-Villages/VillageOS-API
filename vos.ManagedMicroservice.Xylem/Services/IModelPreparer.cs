using System.Net.Http.Headers;

namespace vos.ManagedMicroservice.Xylem.Services;

// Prepares the model before a new-model ingest. Returns null on success, or an error message.
// Kept behind an interface so the handler's new-model decision is unit-tested without a live broker.
public interface IModelPreparer
{
    Task<string?> ClearModelAsync(CancellationToken ct);
}

// Clears the current model via DELETE /api/model so an ingest can build a fresh one.
public sealed class HttpModelPreparer : IModelPreparer
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _myceliumUrl;
    private readonly string? _token;

    public HttpModelPreparer(IHttpClientFactory httpFactory, string myceliumUrl, string? token)
    {
        _httpFactory = httpFactory;
        _myceliumUrl = myceliumUrl;
        _token = token;
    }

    public async Task<string?> ClearModelAsync(CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Delete, $"{_myceliumUrl.TrimEnd('/')}/api/model");
        if (!string.IsNullOrEmpty(_token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        var resp = await http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode ? null : $"Failed to clear the model for a new-model ingest ({(int)resp.StatusCode}).";
    }
}
