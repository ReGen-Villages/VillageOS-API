using System.Reflection;

namespace vos.Service.Intake.Models;

// A refusal as a page reads it: a code naming what went wrong and the values its words need, so the page
// can say it in the reader's language, beside the English a page that does not know the code shows as it
// is. The codes are a contract with the pages: vos.Trellis/src/api/refusals.ts lists the same ones.
public sealed record Refusal(string Code, string Error, IReadOnlyDictionary<string, object?>? Values = null)
{
    public static IReadOnlyDictionary<string, object?> With(params (string Name, object? Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value);
}

public static class RefusalCode
{
    public const string TooManyRequests = "tooManyRequests";
    public const string ServiceUnavailable = "serviceUnavailable";
    public const string CodeSendingUnavailable = "codeSendingUnavailable";
    public const string SubmissionsUnavailable = "submissionsUnavailable";
    public const string FilesUnavailable = "filesUnavailable";

    public const string PositionMissing = "positionMissing";
    public const string NoParcel = "noParcel";
    public const string QueryMissing = "queryMissing";
    public const string NoSearch = "noSearch";
    public const string NoBasemap = "noBasemap";

    public const string EmailAddressMissing = "emailAddressMissing";
    public const string CodesExhausted = "codesExhausted";
    public const string CodeNotAccepted = "codeNotAccepted";
    public const string TicketMissing = "ticketMissing";
    public const string TicketExpired = "ticketExpired";
    public const string AddressNotVerified = "addressNotVerified";

    public const string SubmissionTooLarge = "submissionTooLarge";
    public const string SubmissionEmpty = "submissionEmpty";
    public const string SubmissionUnreadable = "submissionUnreadable";
    public const string IdentifierInvalid = "identifierInvalid";
    public const string EmailAddressMalformed = "emailAddressMalformed";
    public const string FieldMissing = "fieldMissing";
    public const string FieldTooLong = "fieldTooLong";
    public const string FieldTooMany = "fieldTooMany";
    public const string FieldOutOfRange = "fieldOutOfRange";
    public const string TermUnknown = "termUnknown";
    public const string SharesDisagree = "sharesDisagree";
    public const string HazardsDisagree = "hazardsDisagree";
    public const string SourceDescribedTwice = "sourceDescribedTwice";
    public const string BoundaryTooFewCorners = "boundaryTooFewCorners";
    public const string ModelRefused = "modelRefused";

    public const string ReferenceAndAddressNeeded = "referenceAndAddressNeeded";
    public const string NotYourSubmission = "notYourSubmission";
    public const string ReductionMalformed = "reductionMalformed";

    public const string FileNotAForm = "fileNotAForm";
    public const string NoFile = "noFile";
    public const string FileTooLarge = "fileTooLarge";
    public const string DescriptionTooLong = "descriptionTooLong";
    public const string FilesNotTaken = "filesNotTaken";

    public static IReadOnlyList<string> All { get; } =
    [
        .. typeof(RefusalCode).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!),
    ];
}
