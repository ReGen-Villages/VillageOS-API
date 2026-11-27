using vos.Service.Phloem.Model;
using vos.Service.Shared.Roles;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

internal static class EngineArrangement
{
    public static readonly Role RangeRole = new("range", RoleIdentity.Of("range"));
    public static readonly Role JudgedThingRole = new("judgedThing", RoleIdentity.Of("judgedThing"));

    public static Dictionary<string, object?> PortNamed(string name, string direction, string type) => new()
    {
        [ModelNames.PortName] = name, [ModelNames.Direction] = direction, [ModelNames.PortType] = type,
    };
}
