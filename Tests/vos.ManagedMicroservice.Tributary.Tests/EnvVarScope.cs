namespace vos.ManagedMicroservice.Tributary.Tests;

/// <summary>
/// Test helper that sets process environment variables in its constructor and restores
/// their prior values on <see cref="Dispose"/>. Replaces the <c>AuthEnabledFactory</c>
/// sub-class pattern in the old CoverageGapTests, which used <c>new</c>-shadowed lifecycle
/// methods &mdash; the shadowing didn't reach <c>IAsyncDisposable</c> dispatch through
/// <c>await using</c>, so the per-test env vars leaked. A using-scope sidesteps the
/// inheritance dispatch issue entirely.
///
/// Mirrors the helper in <c>Tests/vos.ManagedMicroservice.Delta.Tests/EnvVarScope.cs</c>;
/// promote to a shared test project if a third service needs it.
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
