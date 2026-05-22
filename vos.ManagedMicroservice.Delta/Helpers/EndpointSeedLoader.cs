using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Delta.Models;

namespace vos.ManagedMicroservice.Delta.Helpers;

/// <summary>
/// Loads the Delta service's <c>seed.json</c> endpoint template. Walks a list of candidate
/// paths and returns the first valid <see cref="RegisterEndpointRequest"/>. Parse failures
/// are logged at Warning and the loader moves to the next candidate; if every candidate is
/// missing or unparseable, it throws.
///
/// Extracted from Program.cs under Feature #5433 / Task #5436. The default candidate-path
/// set lives here as <see cref="DefaultCandidatePaths"/>; tests pass arbitrary paths to
/// <see cref="Load"/> directly.
/// </summary>
public static class EndpointSeedLoader
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The three locations checked at startup. AppContext.BaseDirectory is the bin output
    /// for normal runs; CurrentDirectory covers IDE/dev shells launching from the project
    /// root; the three-up path matches the canonical seed location relative to a built
    /// service binary. Tests pass their own paths to <see cref="Load"/>.
    /// </summary>
    public static IEnumerable<string> DefaultCandidatePaths => new[]
    {
        Path.Combine(AppContext.BaseDirectory, "seed.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "seed.json"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "seed.json")
    };

    /// <summary>
    /// Walk the candidate paths in order. Return the first successfully-parsed seed with a
    /// non-empty <see cref="RegisterEndpointRequest.Name"/>. Missing files are skipped silently;
    /// parse failures log a Warning and move to the next candidate. Throws
    /// <see cref="InvalidOperationException"/> when no candidate yields a valid seed.
    /// </summary>
    public static RegisterEndpointRequest Load(IEnumerable<string> candidatePaths, ILogger logger)
    {
        foreach (var candidate in candidatePaths.Select(Path.GetFullPath))
        {
            if (!File.Exists(candidate))
                continue;

            try
            {
                var content = File.ReadAllText(candidate);
                var seed = JsonSerializer.Deserialize<RegisterEndpointRequest>(content, DeserializeOptions);

                if (seed != null && !string.IsNullOrWhiteSpace(seed.Name))
                    return seed;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse seed file {Path}", candidate);
            }
        }

        throw new InvalidOperationException("Could not load a valid Endpoint seed from seed.json.");
    }

    /// <summary>Convenience wrapper that uses <see cref="DefaultCandidatePaths"/>.</summary>
    public static RegisterEndpointRequest LoadDefault(ILogger logger) => Load(DefaultCandidatePaths, logger);
}
