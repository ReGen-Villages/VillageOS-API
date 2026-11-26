using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

// Asks the platform's fetcher, which holds the provider's address, so no address or limit is written here.
public sealed class GroundHeightReader : MyceliumClientBase
{
    public const int MostPointsInACall = 100;
    public static readonly TimeSpan GapBetweenCalls = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock;
    private readonly string _fetcherSubdomain;
    private readonly SemaphoreSlim _oneCallAtATime = new(1, 1);
    private DateTimeOffset? _lastCallEnded;

    public GroundHeightReader(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, TimeProvider clock,
        LaunchToken? launchToken = null, string? apiKey = null, string fetcherSubdomain = OutsideCalls.FetcherSubdomain)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
    {
        _clock = clock;
        _fetcherSubdomain = fetcherSubdomain;
    }

    public async Task<IReadOnlyList<double>> HeightsAtAsync(string endpointName, IReadOnlyList<Position> points, CancellationToken cancellationToken)
    {
        var heights = new List<double>(points.Count);
        foreach (var chunk in points.Chunk(MostPointsInACall))
            heights.AddRange(await AskSpacedAsync(endpointName, chunk, cancellationToken));
        return heights;
    }

    private async Task<IReadOnlyList<double>> AskSpacedAsync(string endpointName, Position[] points, CancellationToken cancellationToken)
    {
        await _oneCallAtATime.WaitAsync(cancellationToken);
        try
        {
            if (_lastCallEnded is { } ended && _clock.GetUtcNow() - ended is var since && since < GapBetweenCalls)
                await Task.Delay(GapBetweenCalls - since, _clock, cancellationToken);
            try
            {
                return await AskAsync(endpointName, points, cancellationToken);
            }
            finally
            {
                _lastCallEnded = _clock.GetUtcNow();
            }
        }
        finally
        {
            _oneCallAtATime.Release();
        }
    }

    private async Task<IReadOnlyList<double>> AskAsync(string endpointName, Position[] points, CancellationToken cancellationToken)
    {
        var locations = string.Join('|', points.Select(point =>
            string.Create(CultureInfo.InvariantCulture, $"{point.Latitude:F6},{point.Longitude:F6}")));

        HttpResponseMessage response;
        try
        {
            var client = await CreateAuthenticatedClientAsync(CallTimeout);
            response = await client.PostAsJsonAsync($"{MyceliumUrl}/api/endpoints/{Uri.EscapeDataString(_fetcherSubdomain)}",
                new { endpointName, addressParameters = new Dictionary<string, string> { ["locations"] = locations } }, cancellationToken);
        }
        catch (Exception unreached) when (unreached is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new FailureCanBeTriedAgainException($"The heights source could not be reached: {unreached.Message}");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new FailureCanBeTriedAgainException(
                    $"The heights source answered {(int)response.StatusCode}, so the ground is worked out later.",
                    response.Headers.RetryAfter?.Delta);
            if (!response.IsSuccessStatusCode)
                throw new NodeFailedException($"The heights source refused the call with {(int)response.StatusCode}.");

            var answer = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!answer.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array
                || results.GetArrayLength() != points.Length)
                throw new NodeFailedException($"The heights source answered no height for some of the {points.Length} points asked.");

            return [.. results.EnumerateArray().Select(result =>
                result.TryGetProperty("elevation", out var elevation) && elevation.ValueKind == JsonValueKind.Number ? elevation.GetDouble() : 0)];
        }
    }
}
