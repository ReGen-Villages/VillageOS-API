namespace vos.Pages;

/// <summary>
/// The reader could not answer, in words a person can be shown: a page or a Thing the model does not
/// hold, or a page whose description cannot be read.
/// </summary>
public sealed class PageNotReadException(string message) : Exception(message);
