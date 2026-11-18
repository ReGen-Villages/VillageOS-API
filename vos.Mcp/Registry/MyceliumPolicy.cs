namespace vos.Mcp.Registry;

public enum MyceliumPolicy
{
    ReadModel,
    ModifyData,
    ManageRanges,
    WritePropertyFact,
    WritePropertyObservation,
    EditorOrAdmin,
    AdminOnly,
}

public static class MyceliumPolicies
{
    private const string Viewer = "viewer";
    private const string Editor = "editor";
    private const string Admin = "admin";
    private const string Service = "service";

    public static bool IsSatisfiedBy(this MyceliumPolicy policy, string role) => policy switch
    {
        MyceliumPolicy.ReadModel => role is Viewer or Editor or Admin or Service,
        MyceliumPolicy.ModifyData or MyceliumPolicy.WritePropertyFact or MyceliumPolicy.WritePropertyObservation =>
            role is Editor or Admin or Service,
        MyceliumPolicy.ManageRanges or MyceliumPolicy.EditorOrAdmin => role is Editor or Admin,
        MyceliumPolicy.AdminOnly => role is Admin,
        _ => false,
    };

    public static bool Writes(this MyceliumPolicy policy) => policy is not MyceliumPolicy.ReadModel;
}
