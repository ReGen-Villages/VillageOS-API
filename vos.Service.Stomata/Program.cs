using vos.Service.Shared.DagNode;
using vos.Service.Stomata.Services;

return DagNodeHost.Run<StomataNode>(args, "Stomata", "stomata-.log",
    (httpClientFactory, logger, myceliumUrl, launchToken, apiKey) =>
        new StomataNode(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey));

public partial class Program { }
