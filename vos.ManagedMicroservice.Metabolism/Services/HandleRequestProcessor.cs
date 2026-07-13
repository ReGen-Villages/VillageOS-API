using System.Globalization;
using System.Text.Json;
using vos.ManagedMicroservice.Metabolism.Models;

namespace vos.ManagedMicroservice.Metabolism.Services;

public class HandleRequestProcessor
{
    private readonly Metabolism _engine;
    private readonly ILogger<HandleRequestProcessor> _logger;

    public HandleRequestProcessor(Metabolism engine, ILogger<HandleRequestProcessor> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    // Returns (entry, null) on success or (null, errorMessage) on failure.
    public (SimulationEntry? entry, string? error) ProcessHandle(HandleRequest request)
    {
        if (string.IsNullOrEmpty(request.SubjectId) || string.IsNullOrEmpty(request.TargetId))
            return (null, "Missing required fields: subjectId and targetId are required");

        var config = ExtractConfig(request);
        var entry = _engine.Register(config);

        _logger.LogInformation("Registered simulation for {RelId}: {Quantity} {Unit} every {Freq}s",
            config.RelationshipId, config.Quantity, config.Unit, config.FrequencySeconds);

        return (entry, null);
    }

    // Pure extraction of SimulationConfig from a HandleRequest. No side effects.
    internal static SimulationConfig ExtractConfig(HandleRequest request)
    {
        decimal quantity = 1.0m;
        string propertyPath = "quantity";
        string unit = "";
        int frequencySeconds = 60;
        var startUtc = DateTime.UtcNow;
        var endUtc = new DateTime(2099, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        decimal startDelaySeconds = 0m;

        if (request.Properties.HasValue && request.Properties.Value.ValueKind == JsonValueKind.Object)
        {
            var props = request.Properties.Value;

            if (props.TryGetProperty("quantity", out var quantityProp) && quantityProp.ValueKind == JsonValueKind.Number)
                quantity = quantityProp.GetDecimal();

            if (props.TryGetProperty("propertyPath", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                propertyPath = pathProp.GetString() ?? "quantity";

            if (props.TryGetProperty("unit", out var unitProp) && unitProp.ValueKind == JsonValueKind.String)
                unit = unitProp.GetString() ?? "";

            if (props.TryGetProperty("frequencySeconds", out var freqProp) && freqProp.ValueKind == JsonValueKind.Number)
                frequencySeconds = freqProp.GetInt32();

            if (props.TryGetProperty("startUtc", out var startProp) && startProp.ValueKind == JsonValueKind.String)
                startUtc = DateTime.Parse(startProp.GetString()!, null, DateTimeStyles.RoundtripKind);

            if (props.TryGetProperty("endUtc", out var endProp) && endProp.ValueKind == JsonValueKind.String)
                endUtc = DateTime.Parse(endProp.GetString()!, null, DateTimeStyles.RoundtripKind);

            if (props.TryGetProperty("startDelaySeconds", out var delayProp) && delayProp.ValueKind == JsonValueKind.Number)
                startDelaySeconds = delayProp.GetDecimal();
        }

        return new SimulationConfig(
            request.RelationshipId!, request.SubjectId, request.TargetId,
            request.SubjectName ?? "",
            quantity, unit, propertyPath, frequencySeconds, startUtc, endUtc, startDelaySeconds);
    }
}
