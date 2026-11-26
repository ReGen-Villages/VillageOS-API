using vos.Service.Shared.Roles;

namespace vos.Service.Stomata.Services;

public static class StomataRoles
{
    public static readonly Role WindDirectionSector = new("windDirectionSector", RoleIdentity.Of("windDirectionSector"));
    public static readonly Role WindSpeedClass = new("windSpeedClass", RoleIdentity.Of("windSpeedClass"));
    public static readonly Role WindRoseCell = new("windRoseCell", RoleIdentity.Of("windRoseCell"));

    public static readonly IReadOnlyList<Role> WindRose = [WindDirectionSector, WindSpeedClass, WindRoseCell];
}
