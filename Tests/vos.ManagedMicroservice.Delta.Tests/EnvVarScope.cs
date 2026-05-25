namespace vos.ManagedMicroservice.Delta.Tests;

/// <summary>
/// Test helper that sets process environment variables in its constructor and restores
/// their prior values on <see cref="Dispose"/>. Used by tests that need to inject
/// per-test env vars (e.g. DELTA_SIGNING_KEY for auth-wireup tests) without leaking them
/// to subsequent tests in the same collection.
///
/// A using-scope avoids the <c>IAsyncDisposable</c> dispatch issue that a
/// <c>new</c>-shadowed lifecycle method on a <c>WebApplicationFactory</c> subclass has
/// &mdash; the shadowing doesn't reach <c>IAsyncDisposable</c> dispatch through
/// <c>await using</c>, so per-test env vars leak.
/// </summary>
internal sealed class EnvVarScope : IDisposable
{
    private readonly Dictionary<string, string?> _originals = new();

    public EnvVarScope(params (string Name, string? Value)[] vars)
    {
        foreach (var (name, value) in vars)
        {
            _originals[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, original) in _originals)
            Environment.SetEnvironmentVariable(name, original);
    }
}
