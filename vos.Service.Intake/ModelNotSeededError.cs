namespace vos.Service.Intake;

// The model this service writes into is missing something it was deployed expecting. Distinct
// from SubmissionError, which names a field the submitter can correct: nobody filling in a
// form can fix this, so the message goes to the log and the caller is told only that the service could
// not accept the submission.
public sealed class ModelNotSeededError(string message) : Exception(message);
