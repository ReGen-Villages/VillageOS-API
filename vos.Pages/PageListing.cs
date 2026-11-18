namespace vos.Pages;

/// <param name="Address">The segment the console addresses the page by.</param>
/// <param name="Readable">False where the page's description could not be read; the page is still listed.</param>
public sealed record PageListing(string Id, string Name, string Address, bool Readable);

public sealed record PageScope(string Id, string Name);
