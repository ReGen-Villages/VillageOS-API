using vos.Service.Shared.DagNode;
using vos.Service.Transect.Services;

return DagNodeHost.Run<TransectNode>(args, "Transect", "transect-.log",
    (httpClientFactory, logger, myceliumUrl, launchToken, apiKey) =>
        new TransectNode(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey));

public partial class Program { }
