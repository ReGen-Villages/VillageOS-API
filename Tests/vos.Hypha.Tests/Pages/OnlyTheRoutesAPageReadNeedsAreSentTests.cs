using vos.Hypha.Tools.Pages;
using Xunit;

namespace vos.Hypha.Tests.Pages;

// A page description is the model's data, and a service binding in it names the route it posts to. The
// reader hands that route on as written, so what is sent with the key is decided here and nowhere else.
public class OnlyTheRoutesAPageReadNeedsAreSentTests
{
    private const string AThing = "1c8b31fc-7770-50e8-9a9c-b21491c9bc61";

    [Theory]
    [InlineData("/api/states/RunningLow/things")]
    [InlineData("/api/states/Running%20low/things?type=Reservoir&countOnly=true")]
    [InlineData("/api/states/RunningLow/things?within=" + AThing + "&withinPredicate=drains+into")]
    [InlineData("/api/states/Low%20(north)!/things?type=%C3%85sen%27s+%28north%29+%7Espring*+%26+co%21")]
    [InlineData("/api/things/" + AThing + "/ranges")]
    public void A_read_a_page_makes_may_be_fetched(string path) =>
        Assert.Null(PageReadRoutes.RefusalToGet(path));

    [Theory]
    [InlineData("/api/temporal/aggregate")]
    [InlineData("/api/temporal/reduce")]
    [InlineData("/api/endpoints/inflow")]
    [InlineData("/api/endpoints/inflow-by-catchment")]
    public void A_question_a_page_asks_may_be_posted(string path) =>
        Assert.Null(PageReadRoutes.RefusalToPost(path));

    [Theory]
    [InlineData("/api/things/" + AThing)]
    [InlineData("/api/things/" + AThing + "/properties")]
    [InlineData("/api/things/not-an-identifier/ranges")]
    [InlineData("/api/things/" + AThing + "/ranges/RunningLow")]
    [InlineData("/api/things/" + AThing + "/ranges?x=1")]
    [InlineData("/api/states/RunningLow")]
    [InlineData("/api/states/a/b/things")]
    [InlineData("/api/model")]
    [InlineData("/api/accounts")]
    [InlineData("/api/endpoints/inflow")]
    [InlineData("/api/temporal/reduce")]
    public void Any_other_route_is_refused_for_a_fetch_and_the_refusal_names_it(string path) =>
        Assert.Contains(path, PageReadRoutes.RefusalToGet(path));

    [Theory]
    [InlineData("/api/things")]
    [InlineData("/api/things/" + AThing + "/properties/levelMetres/fact")]
    [InlineData("/api/relationships")]
    [InlineData("/api/model")]
    [InlineData("/api/model/prune")]
    [InlineData("/api/accounts")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/operator-activity")]
    [InlineData("/api/mycelium/services/" + AThing + "/request")]
    [InlineData("/api/snapshots")]
    [InlineData("/api/states/RunningLow/things")]
    [InlineData("/api/endpoints")]
    [InlineData("/api/endpoints/")]
    [InlineData("/api/endpoints/inflow/extra")]
    [InlineData("/api/endpoints/inflow?then=/api/model")]
    [InlineData("/api/temporal/reduce/extra")]
    [InlineData("/api/temporal/aggregate?x=1")]
    public void Any_other_route_is_refused_for_a_post_and_the_refusal_names_it(string path) =>
        Assert.Contains(path, PageReadRoutes.RefusalToPost(path));

    [Theory]
    [InlineData("https://elsewhere.example/api/endpoints/inflow")]
    [InlineData("http://127.0.0.1:9/api/endpoints/inflow")]
    [InlineData("//elsewhere.example/api/endpoints/inflow")]
    [InlineData("api/endpoints/inflow")]
    [InlineData("inflow")]
    [InlineData("")]
    [InlineData("/api/endpoints/../model")]
    [InlineData("/api/endpoints/..%2Fmodel")]
    [InlineData("/api/endpoints/%2e%2e/model")]
    [InlineData("/api/endpoints/inflow%2Fextra")]
    [InlineData("/api/endpoints/inflow\\..\\model")]
    [InlineData("/api/endpoints/inflow#/api/model")]
    [InlineData("/api/endpoints/in flow")]
    [InlineData("/api/endpoints/inflow\r\nX-Injected: 1")]
    [InlineData("/api/endpoints/@elsewhere.example")]
    [InlineData("/API/endpoints/inflow")]
    public void An_address_that_is_not_a_plain_path_of_this_Mycelium_is_never_followed(string path)
    {
        Assert.NotNull(PageReadRoutes.RefusalToPost(path));
        Assert.NotNull(PageReadRoutes.RefusalToGet(path));
    }
}
