using vos.Service.Shared.DagNode;
using vos.Service.Watershed.Services;

return DagNodeHost.Run<WatershedNode>(args, "Watershed", "watershed-.log",
    (httpClientFactory, logger, myceliumUrl, launchToken, apiKey) =>
        new WatershedNode(httpClientFactory, logger, myceliumUrl,
            new GroundHeightReader(httpClientFactory, logger, myceliumUrl, TimeProvider.System, launchToken, apiKey),
            TimeProvider.System, launchToken, apiKey));

public partial class Program { }
