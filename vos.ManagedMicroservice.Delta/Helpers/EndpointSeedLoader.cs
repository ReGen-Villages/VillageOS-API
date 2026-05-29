using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Helpers;

/// <summary>
/// Loads the Delta service's endpoint-template seed — a single model document
/// (<see cref="EndpointSeedModel"/>: things + relationships) — and assembles it into a validated
/// <see cref="EndpointSeedGraph"/>. Walks a list of candidate <c>seed.json</c> paths and uses the
/// first that exists. A found-but-unparseable seed, or a structurally invalid graph, throws so a
/// misconfigured deployment fails fast at boot.
///
/// Reworked from the single-thing loader under Feature #5465 / Task #5466: the seed is now a
/// model fragment whose <c>is</c> relationships express the template hierarchy.
/// </summary>
public static class EndpointSeedLoader
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The three locations checked at startup. AppContext.BaseDirectory is the bin output for normal
    /// runs; CurrentDirectory covers IDE/dev shells launching from the project root; the three-up path
    /// matches the canonical seed location relative to a built service binary. Tests pass their own paths.
    /// </summary>
    public static IEnumerable<string> DefaultCandidatePaths => new[]
    {
        Path.Combine(AppContext.BaseDirectory, "seed.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "seed.json"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "seed.json")
    };

    /// <summary>
    /// Walk the candidate paths in order; load the first that exists as an <see cref="EndpointSeedModel"/>
    /// and return <see cref="EndpointSeedGraph.Build"/> of it. Missing files are skipped. Throws
    /// <see cref="InvalidOperationException"/> when a found seed cannot be parsed, when the graph is
    /// invalid, or when no candidate path exists.
    /// </summary>
    public static EndpointSeedGraph LoadGraph(IEnumerable<string> candidatePaths, ILogger logger)
    {
        foreach (var candidate in candidatePaths.Select(Path.GetFullPath))
        {
            if (!File.Exists(candidate))
                continue;

            EndpointSeedModel? model;
            try
            {
                model = JsonSerializer.Deserialize<EndpointSeedModel>(File.ReadAllText(candidate), DeserializeOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse endpoint seed file '{candidate}'.", ex);
            }

            if (model == null)
                throw new InvalidOperationException($"Endpoint seed file '{candidate}' deserialized to null.");

            logger.LogInformation("Loaded endpoint seed model from {Path} ({Count} template(s))", candidate, model.Things?.Count ?? 0);
            return EndpointSeedGraph.Build(model);
        }

        throw new InvalidOperationException("Could not load a valid Endpoint seed from seed.json.");
    }

    /// <summary>Convenience wrapper that uses <see cref="DefaultCandidatePaths"/>.</summary>
    public static EndpointSeedGraph LoadGraphDefault(ILogger logger) => LoadGraph(DefaultCandidatePaths, logger);
}
