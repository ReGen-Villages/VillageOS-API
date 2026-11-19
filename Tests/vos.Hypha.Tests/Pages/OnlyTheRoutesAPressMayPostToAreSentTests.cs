using vos.Hypha.Tools.Pages;
using Xunit;

namespace vos.Hypha.Tests.Pages;

// The route a press posts to is written in the page description, which any editor of the model can change,
// and the person confirms a press by what it says, so the routes a press may reach are listed here one by one.
public class OnlyTheRoutesAPressMayPostToAreSentTests
{
    private const string AThing = "1c8b31fc-7770-50e8-9a9c-b21491c9bc61";

    [Theory]
    [InlineData("/api/endpoints/echo")]
    [InlineData("/api/endpoints/spring-inspections")]
    [InlineData("/api/endpoints/Quelle%20(Nord)")]
    [InlineData("/api/model/promote")]
    public void A_service_endpoint_or_a_listed_platform_route_is_posted_to(string path) =>
        Assert.Null(PageReadRoutes.RefusalToPress(path));

    [Theory]
    [InlineData("/api/model")]
    [InlineData("/api/model/fragment")]
    [InlineData("/api/model/prune")]
    [InlineData("/api/mycelium/shutdown")]
    [InlineData("/api/auth/administration")]
    [InlineData("/api/relationships")]
    [InlineData("/api/things/" + AThing + "/properties")]
    [InlineData("/api/operator-activity")]
    [InlineData("/api/temporal/reduce")]
    [InlineData("/api/states/RunningLow/things")]
    public void A_platform_route_that_is_not_listed_is_refused_and_the_refusal_names_it(string path) =>
        Assert.Contains(path, PageReadRoutes.RefusalToPress(path));

    [Theory]
    [InlineData("/api/endpoints/../model")]
    [InlineData("/api/endpoints/echo/../../model")]
    [InlineData("/api/endpoints/%2e%2e/model")]
    [InlineData("/api/endpoints/echo%2F..%2Fmodel")]
    [InlineData("/api/endpoints/echo\\..\\model")]
    [InlineData("/api/model/promote/../fragment")]
    [InlineData("//elsewhere.example/api/endpoints/echo")]
    [InlineData("https://elsewhere.example/api/endpoints/echo")]
    [InlineData("/api/endpoints/echo?then=/api/model")]
    [InlineData("/api/model/promote?x=1")]
    [InlineData("/API/model/promote")]
    [InlineData("/api/model/promote/")]
    [InlineData("/api/endpoints/echo\n")]
    [InlineData("/api/endpoints/echo\r\nX-Injected: 1")]
    [InlineData("/api／endpoints／echo")]
    [InlineData("/api/endpoints/")]
    [InlineData("/api/endpoints")]
    [InlineData("/api/endpoints/echo/extra")]
    [InlineData("/api/endpoints/ech o")]
    [InlineData("")]
    public void An_address_that_is_not_one_of_those_routes_written_plainly_is_never_posted_to(string path) =>
        Assert.NotNull(PageReadRoutes.RefusalToPress(path));

    // Tendril turns a `via` that does not begin with a slash into /api/endpoints/<via>, so these are what a
    // page naming an endpoint by name sends.
    [Theory]
    [InlineData("..")]
    [InlineData("echo/../../model")]
    [InlineData("%2e%2e")]
    [InlineData("echo?x")]
    [InlineData("")]
    public void An_endpoint_named_so_as_to_reach_another_route_is_never_posted_to(string via) =>
        Assert.NotNull(PageReadRoutes.RefusalToPress("/api/endpoints/" + via));
}
