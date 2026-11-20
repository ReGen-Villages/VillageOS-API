using vos.Service.Shared.DagNode;
using vos.Service.WaterReserve.Services;

return DagNodeHost.Run<WaterReserveNode>(args, "WaterReserve", "water-reserve-.log",
    (httpClientFactory, logger, myceliumUrl, launchToken, apiKey) =>
        new WaterReserveNode(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey));

public partial class Program { }
