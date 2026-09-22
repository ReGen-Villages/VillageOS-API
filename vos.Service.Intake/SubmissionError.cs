namespace vos.Service.Intake;

// A submission that cannot be written, and why. The message names the field, because whoever
// filled the form in is the one who can correct it.
public sealed class SubmissionError(string message) : Exception(message);
