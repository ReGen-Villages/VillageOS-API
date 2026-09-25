using vos.Service.Intake.Models;

namespace vos.Service.Intake;

// A submission that cannot be written, and why. The message names the field, because whoever
// filled the form in is the one who can correct it; the code and values let a page say so in their language.
public sealed class SubmissionError(
    string code, string message, IReadOnlyDictionary<string, object?>? values = null) : Exception(message)
{
    public Refusal AsRefusal() => new(code, Message, values);
}
