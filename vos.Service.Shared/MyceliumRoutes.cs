namespace vos.Service.Shared;

// Mycelium routes every service shares. Building a path here rather than repeating the literal means a
// route rename is one edit, not one per caller — the previous rename reached the browser client and the
// command-line client but was missed in five services, which then requested a route that no longer existed.
public static class MyceliumRoutes
{
    // A Thing's resolved properties: own plus inherited, own shadowing inherited. Named "properties"
    // because resolved is the unmarked default; the authored buckets are the ones that carry a qualifier.
    public static string ThingProperties(Guid thingId) => $"/api/things/{thingId}/properties";

    // Where a Fact asserting one of a Thing's properties is posted. The property is escaped here so a
    // caller cannot forget to: it is a name from the model, and one containing a slash or a space would
    // otherwise be posted to a route that does not exist.
    public static string ThingPropertyFacts(Guid thingId, string property) =>
        $"/api/things/{thingId}/properties/{Uri.EscapeDataString(property)}/facts";
}
