namespace vos.Pages;

public sealed class PageReaderOptions
{
    // Told what the reader warns about: a question a page description asks that cannot be asked.
    public Action<string>? Warned { get; init; }
}
