namespace vos.Hypha.Registry;

public static class ListingLimits
{
    public const int Default = 50;
    public const int Ceiling = 500;

    public const string ToNarrow =
        "More match than are shown. Narrow what is asked for, or ask for more with limit, up to the ceiling.";

    public static int For(int? asked) => Math.Clamp(asked ?? Default, 1, Ceiling);
}
