using System.Net;
using System.Text;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

[Collection(nameof(CliEnvVarCollection))]
public class ARefusalCarriesThePlatformsReasonTests
{
    private const string ThePlatformsReason = "Invalid criteria: an operator needs a value on its right";

    private static readonly Guid AnyId = Guid.Parse("7f1c2a54-0b0e-4a55-9d57-3f0c8f3f6f10");

    private static readonly Dictionary<string, Func<MyceliumClient, Task>> Routes = new()
    {
        ["list things"] = client => client.GetAllThingsAsync(),
        ["list properties"] = client => client.GetAllPropertiesAsync(),
        ["read a thing"] = client => client.GetThingAsync(AnyId),
        ["create a thing"] = client => client.CreateThingAsync("Spring1", isArchetype: false),
        ["list relationships"] = client => client.GetAllRelationshipsAsync(),
        ["read a relationship"] = client => client.GetRelationshipAsync(AnyId),
        ["create a relationship"] = client => client.CreateRelationshipAsync(AnyId, AnyId, AnyId),
        ["list services"] = client => client.GetAllServicesAsync(),
        ["list connections"] = client => client.GetAllConnectionsAsync(),
        ["read the model as text"] = client => client.GetModelJsonAsync(),
        ["replace the model"] = client => client.SetModelAsync("{}"),
        ["clear the model"] = client => client.ClearModelAsync(),
        ["apply a fragment"] = client => client.ApplyFragmentAsync("{}"),
        ["promote a group"] = client => client.PromoteAsync(AnyId, [], "project.seed.json", "Willow Bend"),
        ["prune a group"] = client => client.PruneAsync(AnyId, []),
        ["engine metrics"] = client => client.GetEngineMetricsAsync(),
        ["engine reactors"] = client => client.GetEngineReactorsAsync(),
        ["snapshot resolution metrics"] = client => client.GetSnapshotResolutionMetricsAsync(),
        ["seed status"] = client => client.GetSeedStatusAsync(),
        ["list library seeds"] = client => client.ListLibrarySeedsAsync(),
        ["load a library seed"] = client => client.LoadLibrarySeedAsync("village"),
        ["save a library seed"] = client => client.SaveLibrarySeedAsync("village"),
        ["reload seeds"] = client => client.ReloadSeedsAsync(),
        ["list endpoints"] = client => client.GetEndpointsAsync(),
        ["list models"] = client => client.ListModelsAsync(),
        ["switch model"] = client => client.SwitchModelAsync(AnyId),
        ["change a password"] = client => client.ChangePasswordAsync(AnyId, "current", "new"),
        ["read the default property mode"] = client => client.GetDefaultPropertyModeAsync(),
        ["read a property's mode"] = client => client.GetPropertyModeAsync(AnyId, "level"),
        ["read the model at a time"] = client => client.GetModelAtTimeAsync(),
        ["read a thing at a time"] = client => client.GetThingAtTimeAsync(AnyId),
        ["property versions"] = client => client.GetPropertyVersionsAsync(AnyId, "level"),
        ["a thing's mutations"] = client => client.GetThingMutationsAsync(AnyId),
        ["the model's mutations"] = client => client.GetModelMutationsAsync(),
        ["a relationship's mutations"] = client => client.GetRelationshipMutationsAsync(AnyId),
        ["create a range"] = client => client.CreateRangeAsync(AnyId, "low", "level <"),
        ["list ranges"] = client => client.GetRangesAsync(AnyId),
        ["read a range"] = client => client.GetRangeAsync(AnyId, "low"),
        ["list states"] = client => client.GetStatesAsync(AnyId),
        ["validate criteria"] = client => client.ValidateCriteriaAsync("level <"),
    };

    public static TheoryData<string> RouteNames()
    {
        var names = new TheoryData<string>();
        foreach (var name in Routes.Keys)
            names.Add(name);
        return names;
    }

    [Theory]
    [MemberData(nameof(RouteNames))]
    public async Task A_refused_call_says_what_the_platform_said(string route)
    {
        var client = AClientThePlatformRefuses();

        var refused = () => Routes[route](client);

        (await refused.Should().ThrowAsync<HttpRequestException>()).Which.Message
            .Should().Contain("400")
            .And.Contain(ThePlatformsReason);
    }

    [Fact]
    public void No_call_in_the_client_checks_the_status_without_reading_the_reason()
    {
        var sources = TaprootSources()
            .Select(source => (Name: Path.GetFileName(source), Text: File.ReadAllText(source)))
            .ToList();

        sources.Where(source => source.Text.Contains($"{nameof(MyceliumClient.EnsureSuccessCarryingTheReasonAsync)}("))
            .Should().NotBeEmpty("the scan must reach the file the checks live in, or it passes over nothing");

        sources
            .Where(source => source.Text.Contains(".EnsureSuccessStatusCode("))
            .Select(source => source.Name)
            .Should().BeEmpty(
                "the plain status check drops the body the platform answered a refusal with; use {0}",
                nameof(MyceliumClient.EnsureSuccessCarryingTheReasonAsync));
    }

    private static MyceliumClient AClientThePlatformRefuses()
    {
        var handler = new MockHttpMessageHandler(request => request.RequestUri!.AbsolutePath == "/api/auth/token"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"token":"a-token"}""", Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    $$"""{"error":"{{ThePlatformsReason}}"}""", Encoding.UTF8, "application/json"),
            });
        return new MyceliumClient("https://localhost:7243", "an-api-key", new HttpClient(handler));
    }

    private static IEnumerable<string> TaprootSources() =>
        new DirectoryInfo(Path.Combine(RepositoryRoot.Find(), "vos.Taproot"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file.FullName))
            .Select(file => file.FullName);

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj");
}
