using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Delta.Models;

namespace vos.Service.Delta.Helpers;

public static class EndpointSeedLoader
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // BaseDirectory is the bin output for normal runs; CurrentDirectory covers IDE/dev shells launching
    // from the project root; the three-up path is the canonical seed location relative to a built binary.
    public static IEnumerable<string> DefaultCandidatePaths => new[]
    {
        Path.Combine(AppContext.BaseDirectory, "seed.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "seed.json"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "seed.json")
    };

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

    public static EndpointSeedGraph LoadGraphDefault(ILogger logger) => LoadGraph(DefaultCandidatePaths, logger);
}
