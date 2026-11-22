using System.Security.Cryptography;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace vos.Hypha.Registry;

public enum ConfirmationAnswer
{
    Yes,
    No,
    ShownButNotAsked,
}

public sealed record Confirmed(ConfirmationAnswer Answer, string Preview, string Digest)
{
    public bool MayWrite => Answer == ConfirmationAnswer.Yes;

    public string Refusal => Answer == ConfirmationAnswer.No
        ? "The person declined, so nothing was written."
        : $"Nothing was written. This is what would change:\n{Preview}\n"
          + $"Show it to the person, and call again with {Confirmation.DigestParameter} set to {Digest} to write it.";
}

// The digest of what was shown travels with the chat application, so nothing is kept here between the call
// that shows a change and the call that writes it.
public static class Confirmation
{
    public const string DigestParameter = "confirm_digest";

    private const string Question = "confirm";

    public static async Task<Confirmed> AskAsync(
        RequestContext<CallToolRequestParams> call, string preview, bool destroys, string? digestGiven,
        CancellationToken cancellationToken)
    {
        var digest = DigestOf(preview);
        Confirmed Answered(ConfirmationAnswer answer) => new(answer, preview, digest);

        if (call.Params?.RequestState == digest
            && call.Params.InputResponses is { } responses
            && responses.TryGetValue(Question, out var response))
            return Answered(Accepted(response.Deserialize(InputResponse.ElicitResultJsonTypeInfo)));

        var asking = new ElicitRequestParams { Message = preview, RequestedSchema = new() };

        var canAskThePerson = call.Server.ClientCapabilities?.Elicitation is not null;

        if (canAskThePerson && call.Server.IsMrtrSupported)
            throw new InputRequiredException(
                new Dictionary<string, InputRequest> { [Question] = InputRequest.ForElicitation(asking) }, digest);

        // SDK 2.4.0 never reaches this, because it turns the exception above into a request to an older client
        // itself; it stays so that, under an SDK which stops doing that, a person who can be asked still is.
        if (canAskThePerson)
            return Answered(Accepted(await call.Server.ElicitAsync(asking, cancellationToken)));

        if (!destroys || digestGiven == digest)
            return Answered(ConfirmationAnswer.Yes);

        return Answered(ConfirmationAnswer.ShownButNotAsked);
    }

    public static string DigestOf(string preview) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(preview)))[..16];

    private static ConfirmationAnswer Accepted(ElicitResult? result) =>
        result?.IsAccepted == true ? ConfirmationAnswer.Yes : ConfirmationAnswer.No;
}
