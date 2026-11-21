namespace vos.Hypha.Registry;

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
    // The role names as Mycelium writes them in a token's role claim. The test that calls each role's listed
    // tools against a real engine is what proves them and the table below.
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
}
