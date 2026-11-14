using System.Text.Json;
using vos.Service.Intake.Models;

namespace vos.Service.Intake.Services;

public sealed record ChosenFeature(string Name, string Geometry, string Kind);

public static class LayerFeatureChoice
{
    public const string FormField = "features";

    public static IReadOnlyDictionary<string, string> Read(string field)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(field);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Unreadable();
        }
        if (root.ValueKind != JsonValueKind.Object) throw Unreadable();

        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var feature in root.EnumerateObject())
        {
            var name = feature.Name.Trim();
            if (name.Length == 0 || feature.Value.ValueKind != JsonValueKind.String
                                 || string.IsNullOrWhiteSpace(feature.Value.GetString())
                                 || !chosen.TryAdd(name, feature.Value.GetString()!.Trim()))
                throw Unreadable();
            if (name.Length > SubmissionLimits.LongestText)
                throw new SubmissionError(RefusalCode.FieldTooLong,
                    $"A feature's name is longer than the {SubmissionLimits.LongestText} characters it holds.",
                    Refusal.With(("field", FormField), ("most", SubmissionLimits.LongestText)));
        }
        return chosen;
    }

    // Every shape bearing a chosen name is stored, since the page that showed the names could not tell two
    // shapes of one name apart either.
    public static IReadOnlyList<ChosenFeature> Choose(
        IReadOnlyList<LayerShape> shapes, IReadOnlyDictionary<string, string> chosen)
    {
        var features = new List<ChosenFeature>();
        foreach (var (name, kind) in chosen)
        {
            var named = shapes.Where(shape => shape.Name == name).ToList();
            if (named.Count == 0)
                throw new SubmissionError(RefusalCode.FeatureNotInFile,
                    $"'{name}' is not a feature this file holds.", Refusal.With(("value", name)));

            foreach (var shape in named)
            {
                if (shape.Corners > SubmissionLimits.MostBoundaryCorners)
                    throw new SubmissionError(RefusalCode.FieldTooMany,
                        $"'{name}' holds at most {SubmissionLimits.MostBoundaryCorners} corners.",
                        Refusal.With(("field", name), ("most", SubmissionLimits.MostBoundaryCorners)));
                features.Add(new ChosenFeature(name, shape.Geometry, kind));
            }
        }

        if (features.Count > SubmissionLimits.MostLayerFeatures)
            throw new SubmissionError(RefusalCode.FieldTooMany,
                $"'{FormField}' holds at most {SubmissionLimits.MostLayerFeatures} features.",
                Refusal.With(("field", FormField), ("most", SubmissionLimits.MostLayerFeatures)));
        return features;
    }

    private static SubmissionError Unreadable() =>
        new(RefusalCode.FeaturesUnreadable,
            $"'{FormField}' is a JSON object from each feature's name to the kind chosen for it, each named once.");
}
