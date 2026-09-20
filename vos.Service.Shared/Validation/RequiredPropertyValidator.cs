namespace vos.Service.Shared.Validation;

public static class RequiredPropertyValidator
{
    // In the order the required keys were given.
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
