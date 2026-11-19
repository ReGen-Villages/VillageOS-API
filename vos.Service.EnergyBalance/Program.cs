using vos.Service.EnergyBalance.Services;
using vos.Service.Shared.DagNode;

return DagNodeHost.Run<EnergyBalanceNode>(args, "EnergyBalance", "energy-balance-.log",
    (httpClientFactory, logger, myceliumUrl, launchToken, apiKey) =>
        new EnergyBalanceNode(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey));

public partial class Program { }
