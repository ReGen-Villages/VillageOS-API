namespace vos.ManagedMicroservice.Shared.Validation;

// Validates that required property keys are present in a provided key set.
public static class RequiredPropertyValidator
{
    // Returns required keys that are missing from provided keys.
    // Output preserves required key order.
    public static IReadOnlyList<string> GetMissingRequiredKeys(
        IEnumerable<string>? requiredKeys,
        IEnumerable<string>? providedKeys,
        IEqualityComparer<string>? comparer = null)
    {
        if (requiredKeys == null)
            return [];

        var keyComparer = comparer ?? StringComparer.Ordinal;
        var provided = new HashSet<string>(providedKeys ?? Enumerable.Empty<string>(), keyComparer);
        var missing = new List<string>();

        foreach (var requiredKey in requiredKeys)
        {
            if (!provided.Contains(requiredKey))
                missing.Add(requiredKey);
        }

        return missing;
    }
}
