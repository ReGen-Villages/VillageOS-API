using vos.Service.Feedback.Configuration;

namespace vos.Service.Feedback;

// One report from arrival to work item: whose it is, whether it is complete, where it goes, and the
// DevOps calls that file it. Nothing reaches DevOps until every one of those has answered.
public sealed class ReportFiling(
    PlatformCallers callers, DevOpsWorkItems devOps, Destinations destinations, TimeProvider time, ILogger<ReportFiling> log)
{
    private const string ServiceNamePrefix = "service:";

    public async Task<IResult> FileAsync(ReportRequest? request, string? token, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(token))
            return SignInRequired();

        var (verdict, holder) = await callers.CheckAsync(token, cancellation);
        if (verdict == CallerVerdict.Unreachable)
            return Answer(StatusCodes.Status503ServiceUnavailable,
                new Refusal(RefusalCode.ServiceUnavailable, "Reports cannot be taken at the moment."));
        if (holder is null)
            return SignInRequired();

        var (report, refusal) = ReportReading.Read(request);
        if (report is null)
            return Answer(StatusCodes.Status400BadRequest, refusal!);

        if (destinations.For(report.Application) is not { } destination)
            return Answer(StatusCodes.Status400BadRequest,
                new Refusal(RefusalCode.ApplicationUnknown, "This service takes no reports from that application."));

        if (ReporterOf(holder, report) is not { } reporter)
            return Answer(StatusCodes.Status400BadRequest,
                new Refusal(RefusalCode.ReporterMissing, "A report passed on by a service must name who made it."));

        try
        {
            var attachmentUrl = report.Screenshot is { } screenshot
                ? await devOps.AttachAsync(destination.Project, screenshot, cancellation)
                : null;
            var patch = WorkItemDocument.For(report, reporter, holder.ModelName, destination, attachmentUrl, time.GetUtcNow());
            var workItem = await devOps.CreateAsync(
                destination.Project, report.Kind == ReportKind.Bug ? destination.BugType : destination.IdeaType, patch, cancellation);

            log.LogInformation("A {Kind} report from {Application} was filed as work item {WorkItem} in {Project}",
                report.Kind, report.Application, workItem, destination.Project);
            return Results.Ok(new { reference = workItem });
        }
        catch (Exception error) when (error is DevOpsRefusedError or HttpRequestException
                                      || (error is TaskCanceledException && !cancellation.IsCancellationRequested))
        {
            log.LogError(error, "A {Kind} report from {Application} could not be filed in {Project}",
                report.Kind, report.Application, destination.Project);
            return Answer(StatusCodes.Status502BadGateway,
                new Refusal(RefusalCode.FilingFailed, "The report could not be filed. Try again later."));
        }
    }

    // A person is filed under the name their token carries, whatever the report says. A service is
    // trusted to name the person it checked, and is named beside them.
    private static Reporter? ReporterOf(TokenHolder holder, ValidReport report) => holder.Kind switch
    {
        TokenHolderKind.Person => new Reporter(holder.Name, holder.Role, Through: null),
        _ when report.Reporter is { } reporter =>
            new Reporter(reporter, Role: null, Through: holder.Name.StartsWith(ServiceNamePrefix, StringComparison.Ordinal)
                ? holder.Name[ServiceNamePrefix.Length..]
                : holder.Name),
        _ => null,
    };

    private static IResult SignInRequired() =>
        Answer(StatusCodes.Status401Unauthorized, new Refusal(RefusalCode.SignInRequired, "Sign in to send a report."));

    public static IResult Answer(int status, Refusal refusal) => Results.Json(refusal, statusCode: status);
}
