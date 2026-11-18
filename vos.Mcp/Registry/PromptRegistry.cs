using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace vos.Mcp.Registry;

// A source answers the prompts it has for one caller at the moment it is asked, so a prompt can depend on
// what the caller's model holds.
public interface IPromptSource
{
    Task<IReadOnlyList<Prompt>> ListAsync(Caller caller, CancellationToken cancellationToken);

    Task<GetPromptResult?> GetAsync(Caller caller, GetPromptRequestParams asked, CancellationToken cancellationToken);
}

public sealed class PromptRegistry(IEnumerable<IPromptSource> sources, CallerSource callers)
{
    public async ValueTask<ListPromptsResult> ListAsync(CancellationToken cancellationToken)
    {
        var caller = await callers.ReadAsync();
        var prompts = new List<Prompt>();
        foreach (var source in sources)
            prompts.AddRange(await source.ListAsync(caller, cancellationToken));

        return new ListPromptsResult { Prompts = prompts };
    }

    public async ValueTask<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken)
    {
        var caller = await callers.ReadAsync();
        foreach (var source in sources)
            if (await source.GetAsync(caller, request.Params!, cancellationToken) is { } prompt)
                return prompt;

        throw new ModelContextProtocol.McpException($"No prompt is named '{request.Params?.Name}'.");
    }
}
