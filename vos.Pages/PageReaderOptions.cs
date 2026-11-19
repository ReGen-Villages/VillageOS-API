namespace vos.Pages;

public sealed record PageReaderOptions
{
    /// <summary>Told what the reader warns about: a question a page description asks that cannot be asked.</summary>
    public Action<string>? Warned { get; init; }
}
