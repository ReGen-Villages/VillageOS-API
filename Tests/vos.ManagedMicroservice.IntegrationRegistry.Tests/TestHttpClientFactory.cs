namespace vos.ManagedMicroservice.IntegrationRegistry.Tests;

internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _httpClient;

    public TestHttpClientFactory(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public HttpClient CreateClient(string name) => _httpClient;
}
