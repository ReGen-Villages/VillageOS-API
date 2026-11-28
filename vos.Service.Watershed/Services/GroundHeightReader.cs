using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

public sealed class GroundHeightReader : MyceliumClientBase
{
    public const int MostPointsInACall = 100;
    public static readonly TimeSpan GapBetweenCalls = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock;
    private readonly string _fetcherSubdomain;
    private readonly SemaphoreSlim _oneCallAtATime = new(1, 1);
    private readonly Lock _line = new();
    private DateTimeOffset? _lastCallEnded;
    private int _callsInLine;
    private TimeSpan _lastCallTook;

    public GroundHeightReader(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, TimeProvider clock,
        LaunchToken? launchToken = null, string? apiKey = null, string fetcherSubdomain = OutsideCalls.FetcherSubdomain)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
    {
        _clock = clock;
        _fetcherSubdomain = fetcherSubdomain;
    }

    public static int CallsFor(int points) => (points + MostPointsInACall - 1) / MostPointsInACall;

    // A step with nothing ahead of it always joins, because waiting would not shorten its own calls.
    public HeightLinePlace Join(int calls, TimeSpan? timeLeft)
    {
        lock (_line)
        {
            var callCost = GapBetweenCalls + _lastCallTook;
            var ahead = _callsInLine;
            if (calls > 0 && ahead > 0 && timeLeft is { } left && (ahead + calls) * callCost > left)
            {
                var wait = TimeSpan.FromSeconds(Math.Max(1, Math.Ceiling((ahead * callCost).TotalSeconds)));
                throw new FailureCanBeTriedAgainException(string.Create(CultureInfo.InvariantCulture,
                    $"The height line is full: {ahead} calls are ahead of this step's {calls}, about {((ahead + calls) * callCost).TotalSeconds:0.#} s " +
                    $"at {callCost.TotalSeconds:0.##} s a call, past the {left.TotalSeconds:0.#} s left of the time the broker allows, " +
                    $"so the ground is worked out after {wait.TotalSeconds:0} s."), wait);
            }
            _callsInLine += calls;
            return new HeightLinePlace(this, calls);
        }
    }

    public async Task<IReadOnlyList<double>> HeightsAtAsync(
        HeightLinePlace place, string endpointName, IReadOnlyList<Position> points, CancellationToken cancellationToken)
    {
        var heights = new List<double>(points.Count);
        foreach (var chunk in points.Chunk(MostPointsInACall))
            heights.AddRange(await AskSpacedAsync(place, endpointName, chunk, cancellationToken));
        return heights;
    }

    private async Task<IReadOnlyList<double>> AskSpacedAsync(
        HeightLinePlace place, string endpointName, Position[] points, CancellationToken cancellationToken)
    {
        await _oneCallAtATime.WaitAsync(cancellationToken);
        try
        {
            if (_lastCallEnded is { } ended && _clock.GetUtcNow() - ended is var since && since < GapBetweenCalls)
                await Task.Delay(GapBetweenCalls - since, _clock, cancellationToken);
            var startedAt = _clock.GetTimestamp();
            try
            {
                return await AskAsync(endpointName, points, cancellationToken);
            }
            finally
            {
                _lastCallEnded = _clock.GetUtcNow();
                lock (_line)
                    _lastCallTook = _clock.GetElapsedTime(startedAt);
                place.Leave(1);
            }
        }
        finally
        {
            _oneCallAtATime.Release();
        }
    }

    private void LeaveTheLine(HeightLinePlace place, int calls)
    {
        lock (_line)
        {
            var leaving = Math.Min(calls, place.CallsLeft);
            place.CallsLeft -= leaving;
            _callsInLine -= leaving;
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

    public sealed class HeightLinePlace : IDisposable
    {
        private readonly GroundHeightReader _reader;

        internal int CallsLeft { get; set; }

        internal HeightLinePlace(GroundHeightReader reader, int calls)
        {
            _reader = reader;
            CallsLeft = calls;
        }

        internal void Leave(int calls) => _reader.LeaveTheLine(this, calls);

        public void Dispose() => Leave(int.MaxValue);
    }
}
