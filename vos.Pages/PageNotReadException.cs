namespace vos.Pages;

// Its message is shown to a person as it stands.
public sealed class PageNotReadException(string message) : Exception(message);
