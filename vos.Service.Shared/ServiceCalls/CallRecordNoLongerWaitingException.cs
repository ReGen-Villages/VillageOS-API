using System.Net;

namespace vos.Service.Shared.ServiceCalls;

// Sending the report again cannot change this answer, so a caller stops.
public sealed class CallRecordNoLongerWaitingException(Guid callRecordId, CallReport report, string body)
    : HttpRequestException($"Call record {callRecordId} is no longer waiting for a '{report.Word}' report: {body}", null, HttpStatusCode.Conflict)
{
    public Guid CallRecordId { get; } = callRecordId;
}
