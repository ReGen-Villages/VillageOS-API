namespace vos.Pages;

// Address is the segment the console addresses the page by. Readable is false where the page's
// description could not be read; the page is still listed.
public sealed record PageListing(string Id, string Name, string Address, bool Readable);

public sealed record PageScope(string Id, string Name);
