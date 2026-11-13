using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using vos.Service.Intake;
using vos.Service.Intake.Configuration;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;

namespace vos.Service.Intake;

internal static class IntakeEndpoints
{
    // The service does not register with Mycelium and is not reachable through the endpoint-forward route.
    // That route resolves where to forward from data in the model, so anything the model happens to name
    // would be within reach of whoever can call it. Submission stays a program of its own, holding its own
    // credential, so the anonymous route below is one service open rather than every endpoint in the model.
    //
    // Nothing here checks an inbound credential, and that is the decision rather than an omission: the
    // route takes a submission from someone who holds none. A verification key wired up but demanded
    // nowhere would read as protection and be none. What stands in its place is in AddressVerification,
    // SubmissionTicket, SubmissionRate and SubmissionLimits.
    public static void MapIntakeEndpoints(this WebApplication app)
    {
        MapForm(app);
        MapParcelAtPosition(app);
        MapPlaceSearch(app);
        MapVerification(app);
        MapTicket(app);
        MapSubmission(app);
        MapFindings(app);
        MapFindingsReduction(app);
        MapBasemapTiles(app);
        MapDocumentUpload(app);
        MapDocumentList(app);
    }

    private static void MapForm(WebApplication app)
    {
        // What a form draws itself with. The page asking holds no credential and so cannot read the model for
        // itself, and publishing that read would hand a stranger every Thing in it — so the service reads it
        // under its own credential and answers with the categories a submission may name and the imagery a map
        // may draw on, and nothing else.
        app.MapGet("/submissions/form", async (
            HttpContext context, HeldDeclarations declarations, ILogger<SubmissionIntakeService> logger) =>
        {
            try
            {
                return Results.Ok(await declarations.ReadAsync(
                    FormOptionsReader.Selector(), FormOptionsReader.Read, context.RequestAborted));
            }
            // A model nobody seeded is a fault in the deployment rather than in the request, answered the way
            // a submission into one is: the caller is a stranger's browser and is told neither what is wrong
            // nor anything about the model it asked about.
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "A form could not be answered: {Reason}", error.Message);
                return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");
            }
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    private static void MapParcelAtPosition(WebApplication app)
    {
        // The legal parcel at a position somebody clicked, before any site exists — which is why the route,
        // like the form, demands no credential: the click is the first act. What bounds it is the same rate
        // limit as everything here, plus the register's own declared bounds, refused before any outbound call.
        app.MapPost("/submissions/parcel-at-position", async (
            HttpContext context, PositionLookupService lookups, ILogger<SubmissionIntakeService> logger) =>
        {
            var asked = await ReadAsync<ParcelAsked>(context);
            if (asked?.Latitude is not { } latitude || asked.Longitude is not { } longitude
                || latitude is < -90 or > 90 || longitude is < -180 or > 180)
                return Refused(context, "the position was missing or not one",
                    Results.BadRequest(new Refusal(RefusalCode.PositionMissing, "'latitude' and 'longitude' are both needed, in degrees.")));

            try
            {
                var answered = await lookups.ParcelAtAsync(latitude, longitude, context.RequestAborted);
                return answered.Outcome switch
                {
                    LookupOutcome.Found => Results.Ok(new
                    {
                        boundary = answered.Parcel!.Boundary,
                        attribution = answered.Parcel.Attribution,
                    }),
                    LookupOutcome.NothingAvailable => Results.Json(
                        new Refusal(RefusalCode.NoParcel, PositionLookupService.NoParcelAvailable),
                        statusCode: StatusCodes.Status404NotFound),
                    _ => Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment."),
                };
            }
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "A parcel lookup could not be answered: {Reason}", error.Message);
                return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");
            }
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    private static void MapPlaceSearch(WebApplication app)
    {
        // Place names for what somebody typed while looking for their land, so a page can put the pin where
        // they mean without asking for coordinates.
        app.MapPost("/submissions/place-search", async (
            HttpContext context, PositionLookupService lookups, ILogger<SubmissionIntakeService> logger) =>
        {
            var asked = await ReadAsync<PlacesAsked>(context);
            var query = asked?.Query?.Trim();
            if (string.IsNullOrEmpty(query) || query.Length > SubmissionLimits.LongestText)
                return Refused(context, "the search had no query a place could be found for",
                    Results.BadRequest(new Refusal(RefusalCode.QueryMissing, "'query' is missing: there is nothing to search for.")));

            try
            {
                var answered = await lookups.PlacesAsync(query, context.RequestAborted);
                return answered.Outcome switch
                {
                    LookupOutcome.Found => Results.Ok(new
                    {
                        places = answered.Places!.Places,
                        attribution = answered.Places.Attribution,
                    }),
                    LookupOutcome.NothingAvailable => Results.Json(
                        new Refusal(RefusalCode.NoSearch, PositionLookupService.NoSearchAvailable),
                        statusCode: StatusCodes.Status404NotFound),
                    _ => Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment."),
                };
            }
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "A place search could not be answered: {Reason}", error.Message);
                return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");
            }
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    private static void MapVerification(WebApplication app)
    {
        // Sending a code is the one thing this service does to somebody who did not ask for it, because the
        // address is a stranger's word for whose mailbox it is. Two things bound that: the rate limit on the
        // caller, and the budget one address has for codes.
        app.MapPost("/submissions/verification", async (
            HttpContext context,
            AddressVerification verification,
            IVerificationMailer mailer,
            ILogger<SubmissionIntakeService> logger) =>
        {
            var asked = await ReadAsync<VerificationAsked>(context);
            if (asked?.EmailAddress is not { } emailAddress)
                return Refused(context, "no address was given to verify",
                    Results.BadRequest(new Refusal(RefusalCode.EmailAddressMissing, "'emailAddress' is missing: there is nowhere to send a code.")));

            try
            {
                SubmissionLimits.EmailAddress(emailAddress, "emailAddress");
            }
            catch (SubmissionError error)
            {
                return Refused(context, error.Message, Results.BadRequest(error.AsRefusal()));
            }

            if (verification.CodeFor(emailAddress) is not { } code)
                return Refused(context, "the address has been sent as many codes as the window allows",
                    Results.Json(
                        new Refusal(RefusalCode.CodesExhausted, "That address has been sent as many codes as it can be for now."),
                        statusCode: StatusCodes.Status429TooManyRequests));

            try
            {
                await mailer.SendAsync(emailAddress, code, context.RequestAborted);
            }
            catch (Exception error) when (!context.RequestAborted.IsCancellationRequested)
            {
                // The address is a stranger's, so a server refusing it says nothing about this deployment
                // being unwell — but neither this service nor the submitter can tell the two apart, and what
                // went wrong belongs where whoever runs the deployment reads it rather than in the answer.
                // The budget goes back, or three failures nobody saw would lock the address out for the hour.
                verification.NothingWasSent(emailAddress);
                logger.LogError(error, "A verification code could not be sent: {Reason}", error.Message);
                return Unavailable(RefusalCode.CodeSendingUnavailable, "This service cannot send a code at the moment.");
            }

            return Results.Accepted();
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    private static void MapTicket(WebApplication app)
    {
        app.MapPost("/submissions/ticket", async (
            HttpContext context, AddressVerification verification, SubmissionTicket tickets) =>
        {
            var answered = await ReadAsync<VerificationAnswered>(context);
            if (answered?.EmailAddress is not { } emailAddress)
                return Refused(context, "no address was given with the code",
                    Results.BadRequest(new Refusal(RefusalCode.EmailAddressMissing, "'emailAddress' is missing: a ticket is issued against one.")));

            if (verification.WhyRefused(emailAddress, answered.Code) is { } wrongCode)
                return Refused(context, "the code was not accepted",
                    Results.Json(new Refusal(RefusalCode.CodeNotAccepted, wrongCode), statusCode: StatusCodes.Status403Forbidden));

            return Results.Ok(new
            {
                ticket = tickets.Issue(emailAddress),
                validForSeconds = (int)SubmissionTicket.ValidFor.TotalSeconds,
            });
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    private static void MapSubmission(WebApplication app)
    {
        app.MapPost("/submissions", async (
            HttpContext context,
            SubmissionIntakeService intake,
            SubmissionTicket tickets,
            ILogger<SubmissionIntakeService> logger) =>
        {
            if (context.Request.ContentLength > SubmissionLimits.MaximumBodyBytes)
                return Refused(context, "the body is beyond the cap",
                    Results.Json(
                        new Refusal(RefusalCode.SubmissionTooLarge, "The submission is larger than this service takes."),
                        statusCode: StatusCodes.Status413PayloadTooLarge));

            var presented = context.Request.Headers[SubmissionTicket.HeaderName].ToString();
            if (tickets.WhyRefused(presented) is { } notFromAForm)
                return Refused(context, "the ticket was not accepted",
                    Results.Json(notFromAForm, statusCode: StatusCodes.Status403Forbidden));

            using var reader = new StreamReader(context.Request.Body);
            var document = await reader.ReadToEndAsync(context.RequestAborted);

            try
            {
                var submission = SubmissionReader.Read(document);

                // A submission naming no address falls through to the composer, which refuses it by name. A
                // ticket check first would answer a missing field with a rule about a ticket.
                if (submission.Contact?.EmailAddress?.Trim() is { Length: > 0 } claimed
                    && !tickets.WasIssuedFor(presented, claimed))
                    return Refused(context, "the ticket was issued against a different address",
                        Results.Json(
                            new Refusal(RefusalCode.AddressNotVerified, "This submission names an address that was not the one verified."),
                            statusCode: StatusCodes.Status403Forbidden));

                var accepted = await intake.SubmitAsync(submission, context.RequestAborted);
                logger.LogInformation("Submission {Reference} was written into the model", accepted.Reference);

                // A fresh ticket rides back on every act performed with a live one, so a person adjusting
                // their submission stays in the exchange they already completed while an abandoned ticket
                // still dies at the age it always did. The address is the one the presented ticket was
                // checked against above.
                context.Response.Headers[SubmissionTicket.HeaderName] =
                    tickets.Issue(submission.Contact!.EmailAddress!.Trim());
                return Results.Ok(new { reference = accepted.Reference });
            }
            // Something whoever filled the form in can correct, and the message names the field rather than
            // quoting what was in it.
            catch (SubmissionError error)
            {
                return Refused(context, error.Message, Results.BadRequest(error.AsRefusal()));
            }
            // The submission was well formed and the deployment was not ready for it. Answering 400 would tell
            // whoever filled the form in to correct something they cannot reach, and this route takes anonymous
            // submissions, so what is wrong goes to the log rather than into the response.
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "A submission could not be accepted: {Reason}", error.Message);
                return Unavailable(RefusalCode.SubmissionsUnavailable, "This service cannot accept submissions at the moment.");
            }
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    private static void MapFindings(WebApplication app)
    {
        // The one thing the platform gives back. A submission ends at a reference today, and everything the
        // analysis made of the land is visible only to somebody signed in — so this answers the person who
        // submitted it, and nobody else.
        //
        // What stands in place of an account is the pair: the reference names one submission, and the ticket
        // says somebody answered a code sent to one mailbox. Neither alone establishes anything — a reference
        // is guessable and a ticket says nothing about which submission — so the service reads the address
        // that submission names and refuses unless the two agree.
        app.MapPost("/submissions/findings", async (
            HttpContext context,
            SubmissionFindingsService findings,
            SubmissionTicket tickets,
            ILogger<SubmissionFindingsService> logger) =>
        {
            var presented = context.Request.Headers[SubmissionTicket.HeaderName].ToString();
            if (tickets.WhyRefused(presented) is { } notFromAForm)
                return Refused(context, "the ticket was not accepted",
                    Results.Json(notFromAForm, statusCode: StatusCodes.Status403Forbidden));

            var asked = await ReadAsync<FindingsAsked>(context);
            if (asked?.EmailAddress is not { } emailAddress || asked.SubmissionId is not { } submissionId)
                return Refused(context, "the reference or the address was missing",
                    Results.BadRequest(new Refusal(RefusalCode.ReferenceAndAddressNeeded,
                        "'submissionId' and 'emailAddress' are both needed: one names the submission, "
                        + "the other is what the ticket was issued against.")));

            if (!tickets.WasIssuedFor(presented, emailAddress))
                return Refused(context, "the ticket was issued against a different address",
                    Results.Json(
                        new Refusal(RefusalCode.AddressNotVerified, "This request names an address that was not the one verified."),
                        statusCode: StatusCodes.Status403Forbidden));

            try
            {
                var read = await findings.ReadAsync(submissionId, emailAddress, context.RequestAborted);
                if (read is null)
                    return Refused(context, "the reference and the address name no submission",
                        Results.Json(
                            new Refusal(RefusalCode.NotYourSubmission, SubmissionFindingsService.NotYourSubmission),
                            statusCode: StatusCodes.Status404NotFound));

                // As on a submission: a fresh ticket on every act performed with a live one, so a page a
                // person keeps adjusting and re-reading does not send them back to their mailbox mid-way.
                context.Response.Headers[SubmissionTicket.HeaderName] = tickets.Issue(emailAddress);
                return Results.Ok(read);
            }
            // As a submission into an unseeded model is answered: the caller is a stranger's browser and is
            // told neither what is wrong nor anything about the model it asked about.
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "Findings could not be answered: {Reason}", error.Message);
                return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");
            }
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    private static void MapFindingsReduction(WebApplication app)
    {
        // The two reads the tile design adds to the public page, proxied because the page holds no credential
        // and reaches this service alone. The reduction is under the ticket and about the submission's own
        // site, which the service supplies; the tiles are anonymous like the position lookups, since the
        // opening map is drawn before anything has been verified.
        app.MapPost("/findings/{submissionId}/reduce", async (
            string submissionId,
            HttpContext context,
            SubmissionFindingsService findings,
            SubmissionTicket tickets,
            ILogger<SubmissionFindingsService> logger) =>
        {
            var presented = context.Request.Headers[SubmissionTicket.HeaderName].ToString();
            if (tickets.WhyRefused(presented) is { } notFromAForm)
                return Refused(context, "the ticket was not accepted",
                    Results.Json(notFromAForm, statusCode: StatusCodes.Status403Forbidden));

            JsonElement question;
            try
            {
                question = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
            }
            catch (JsonException)
            {
                question = default;
            }
            if (question.ValueKind != JsonValueKind.Object)
                return Refused(context, "the question was not a JSON object",
                    Results.BadRequest(new Refusal(RefusalCode.ReductionMalformed, "The body is the history reduction's request: 'property', 'windowSeconds' and 'steps'.")));

            try
            {
                var answered = await findings.ReduceAsync(
                    submissionId, address => tickets.WasIssuedFor(presented, address), question, context.RequestAborted);
                if (answered is not { } answer)
                    return Refused(context, "the reference and the ticket name no submission",
                        Results.Json(new Refusal(RefusalCode.NotYourSubmission, SubmissionFindingsService.NotYourSubmission), statusCode: StatusCodes.Status404NotFound));
                if (answer.Status >= 500)
                    return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");

                context.Response.Headers[SubmissionTicket.HeaderName] = tickets.Issue(answer.Address);
                return Results.Content(answer.Body, "application/json", statusCode: answer.Status);
            }
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "A reduction could not be answered: {Reason}", error.Message);
                return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");
            }
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    private static void MapBasemapTiles(WebApplication app)
    {
        app.MapGet("/basemaps/{basemap}/{z:int}/{x:int}/{y:int}", async (
            string basemap, int z, int x, int y, HttpContext context, BasemapTileService tiles, ILogger<BasemapTileService> logger) =>
        {
            try
            {
                var served = await tiles.TileAsync(basemap, z, x, y, context.RequestAborted);
                switch (served.Outcome)
                {
                    case TileOutcome.Served:
                        if (served.CacheLife is { } life)
                            context.Response.Headers.CacheControl = $"public, max-age={(long)life.TotalSeconds}";
                        return Results.Bytes(served.Bytes!, served.ContentType);
                    case TileOutcome.NoSuchBasemap:
                        return Refused(context, "no basemap of that name is served",
                            Results.Json(new Refusal(RefusalCode.NoBasemap, "No basemap of that name is served here."), statusCode: StatusCodes.Status404NotFound));
                    default:
                        return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");
                }
            }
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "A tile could not be answered: {Reason}", error.Message);
                return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");
            }
        }).RequireRateLimiting(TileRate.PolicyName);
    }

    private static void MapDocumentUpload(WebApplication app)
    {
        // The files a submitter shares after the report. The bytes go to the store beside this service and the
        // model gets the Thing it declares for one; both under the ticket, since a file is about one
        // submitter's land. The review page lists a submission's files from the model itself.
        app.MapPost("/submissions/{submissionId}/documents", async (
            string submissionId, HttpContext context, SharedDocumentService documents, SubmissionTicket tickets,
            ILogger<SharedDocumentService> logger) =>
        {
            var presented = context.Request.Headers[SubmissionTicket.HeaderName].ToString();
            if (tickets.WhyRefused(presented) is { } notFromAForm)
                return Refused(context, "the ticket was not accepted",
                    Results.Json(notFromAForm, statusCode: StatusCodes.Status403Forbidden));
            if (!context.Request.HasFormContentType)
                return Refused(context, "the body was not a form",
                    Results.BadRequest(new Refusal(RefusalCode.FileNotAForm, "A file is sent as a form: 'file' and, beside it, 'description'.")));

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Refused(context, "no file was sent",
                    Results.BadRequest(new Refusal(RefusalCode.NoFile, "The form carries no 'file', or an empty one.")));
            if (file.Length > SharedDocumentService.MaximumFileBytes)
                return Refused(context, "the file was too large",
                    Results.Json(new Refusal(RefusalCode.FileTooLarge,
                        $"A file may be at most {SharedDocumentService.MaximumFileBytes / (1024 * 1024)} MB.",
                        Refusal.With(("megabytes", SharedDocumentService.MaximumFileBytes / (1024 * 1024)))),
                        statusCode: StatusCodes.Status413PayloadTooLarge));
            var description = form["description"].ToString();
            if (description.Length > SubmissionLimits.LongestProse)
                return Refused(context, "the description was too long",
                    Results.BadRequest(new Refusal(RefusalCode.DescriptionTooLong,
                        $"A description is at most {SubmissionLimits.LongestProse} characters.",
                        Refusal.With(("characters", SubmissionLimits.LongestProse)))));

            try
            {
                await using var bytes = file.OpenReadStream();
                var shared = await documents.ShareAsync(
                    submissionId, address => tickets.WasIssuedFor(presented, address),
                    Path.GetFileName(file.FileName), file.ContentType, description, bytes, context.RequestAborted);
                switch (shared.Outcome)
                {
                    case ShareOutcome.Shared:
                        context.Response.Headers[SubmissionTicket.HeaderName] = tickets.Issue(shared.Address!);
                        return Results.Json(shared.Document, statusCode: StatusCodes.Status201Created);
                    case ShareOutcome.NotTakenHere:
                        return Refused(context, "this model takes no shared file",
                            Results.Json(new Refusal(RefusalCode.FilesNotTaken, "Files are not taken here: the model declares no place for one."),
                                statusCode: StatusCodes.Status404NotFound));
                    default:
                        return Refused(context, "the reference and the ticket name no submission",
                            Results.Json(new Refusal(RefusalCode.NotYourSubmission, SubmissionFindingsService.NotYourSubmission), statusCode: StatusCodes.Status404NotFound));
                }
            }
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException or SubmissionError
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "A shared file could not be taken: {Reason}", error.Message);
                return Unavailable(RefusalCode.FilesUnavailable, "This service cannot take a file at the moment.");
            }
        }).RequireRateLimiting(SubmissionRate.PolicyName)
          // The service-wide body limit is a form's; a file is allowed what a file is allowed, and the
          // route refuses what is over that itself, with the reason.
          .WithMetadata(new RequestSizeLimitAttribute(SharedDocumentService.MaximumRequestBytes));
    }

    private static void MapDocumentList(WebApplication app)
    {
        app.MapGet("/submissions/{submissionId}/documents", async (
            string submissionId, HttpContext context, SharedDocumentService documents, SubmissionTicket tickets,
            ILogger<SharedDocumentService> logger) =>
        {
            var presented = context.Request.Headers[SubmissionTicket.HeaderName].ToString();
            if (tickets.WhyRefused(presented) is { } notFromAForm)
                return Refused(context, "the ticket was not accepted",
                    Results.Json(notFromAForm, statusCode: StatusCodes.Status403Forbidden));
            try
            {
                var listed = await documents.ListAsync(
                    submissionId, address => tickets.WasIssuedFor(presented, address), context.RequestAborted);
                if (listed is not { } answer)
                    return Refused(context, "the reference and the ticket name no submission",
                        Results.Json(new Refusal(RefusalCode.NotYourSubmission, SubmissionFindingsService.NotYourSubmission), statusCode: StatusCodes.Status404NotFound));
                context.Response.Headers[SubmissionTicket.HeaderName] = tickets.Issue(answer.Address);
                return Results.Ok(new { documents = answer.Documents });
            }
            catch (Exception error) when (error is ModelNotSeededError or HttpRequestException
                                          || (error is TaskCanceledException
                                              && !context.RequestAborted.IsCancellationRequested))
            {
                logger.LogError(error, "Shared files could not be listed: {Reason}", error.Message);
                return Unavailable(RefusalCode.ServiceUnavailable, "This service cannot answer at the moment.");
            }
        }).RequireRateLimiting(SubmissionRate.PolicyName);
    }

    // Not the requester's to correct, so nothing is logged as a refusal: whatever went wrong was logged where
    // it was caught.
    private static IResult Unavailable(string code, string error) =>
        Results.Json(new Refusal(code, error), statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult Refused(HttpContext context, string reason, IResult answer)
    {
        LogRefusal(context, reason);
        return answer;
    }

    // The two verification bodies are an address and a code. Anything that does not read as one is nothing
    // rather than an exception, and the route says which field is missing.
    private static async Task<T?> ReadAsync<T>(HttpContext context) where T : class
    {
        if (context.Request.ContentLength > SubmissionLimits.MaximumVerificationBytes) return null;

        try { return await context.Request.ReadFromJsonAsync<T>(context.RequestAborted); }
        catch (JsonException) { return null; }
        catch (BadHttpRequestException) { return null; }
    }

    // A refusal is logged by why it was refused and by where it came from, and never by what was submitted.
    // Contact details arrive on this route by design, and a log line is the one place they would leave the
    // model behind — see "Handling personal data" in docs/LAND_INTAKE.md.
    internal static void LogRefusal(HttpContext context, string reason) =>
        context.RequestServices.GetRequiredService<ILogger<SubmissionIntakeService>>()
            .LogWarning("A submission was refused: {Reason}, source {Source}",
                reason, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}
