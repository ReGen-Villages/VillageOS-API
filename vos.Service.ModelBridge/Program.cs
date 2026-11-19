using vos.Service.ModelBridge.Services;
using vos.Service.Shared.DagNode;

return DagNodeHost.Run<ModelBridgeNode>(args, "ModelBridge", "model-bridge-.log",
    (httpClientFactory, logger, myceliumUrl, launchToken, apiKey) =>
        new ModelBridgeNode(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey));

public partial class Program { }
