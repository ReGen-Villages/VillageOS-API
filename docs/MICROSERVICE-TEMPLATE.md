# ManagedMicroservice Template

Every ManagedMicroservice in this repo (`Echo`, `Tributary`, `Delta`, `Metabolism`) follows the same shape. **Echo is the canonical reference implementation** — the simplest of the four. When adding a new microservice, copy Echo's structure and the test patterns described here.

## Production-code shape

A ManagedMicroservice is a `Microsoft.NET.Sdk.Web` minimal-API binary with three files at minimum:

```
vos.ManagedMicroservice.<Name>/
├── Configuration/
│   └── CliArgs.cs           ← record + Parse(string[]) + UsageMessage
├── Services/
│   └── BrokerClient.cs      ← thin subclass of vos.ManagedMicroservice.Shared.BrokerClientBase
├── Helpers/                 ← optional: pure functions extracted from Program.cs
│   └── <Name>.cs            ← static class, no AspNetCore dependency, fully unit-tested
└── Program.cs               ← top-level statements: parse args → build app → register endpoints → run
```

Project references: `vos.Auth.Shared` (inbound JWT validation) and `vos.ManagedMicroservice.Shared` (broker client base + validators, plus the contract-validation foundation and request-pipeline middleware from Feature #5419 / Feature #5426 — see [`CONTRACT-VALIDATION.md`](CONTRACT-VALIDATION.md)). The microservice does **not** depend on `vos.Core` or `vos.Application`.

### CliArgs

A C# `record` with the standard six fields. Required: `Port`, `BrokerUrl`. Optional: `Token`, `SigningKey`, `Issuer`, `Audience`. `Parse` returns `null` on missing/invalid input. `UsageMessage` mentions every flag.

See `vos.ManagedMicroservice.Echo/Configuration/CliArgs.cs` for the canonical shape.

### BrokerClient

A thin subclass of `vos.ManagedMicroservice.Shared.BrokerClientBase`. The base class owns:
- `HandlerId` (fresh `Guid` per process)
- `BrokerUrl`
- `GetTokenAsync()` (uses `--token` if set, otherwise the legacy `/api/auth/token` endpoint)
- `CreateAuthenticatedClientAsync(timeout?)` (returns `HttpClient` with Bearer auth)
- `RegisterAsync(port, serviceName, startCommand)` (POSTs the registration envelope)
- `DeregisterAsync()` (DELETE `/api/broker/services/{HandlerId}`)

The subclass's job is to provide a service-specific `RegisterAsync(port)` overload that calls base `RegisterAsync` with its own `(serviceName, startCommand)`.

Echo's example:

```csharp
public class BrokerClient : BrokerClientBase
{
    public BrokerClient(IHttpClientFactory http, ILogger<BrokerClient> log, string brokerUrl, string? token = null)
        : base(http, log, brokerUrl, token) { }

    public Task<bool> RegisterAsync(int port)
        => RegisterAsync(port, "Echo", "endpoint-service");
}
```

### Helpers (pure logic out of Program.cs)

When `Program.cs` accumulates inline static helpers — JSON-element coercion, dictionary lookups, file-loading with fallback paths, value-normalization switches — extract them into `vos.ManagedMicroservice.<Name>/Helpers/<Name>.cs` as a static class. This pulls the testable surface off the WebApplicationFactory integration-test path and onto fast unit tests that assert returned values directly.

Worked examples from Feature #5433 / Task #5436:

- `vos.ManagedMicroservice.Delta/Helpers/JsonValueCoercion.cs` — `CoerceToString(object?)`, `TryGetPropertyValue(IDictionary, string, out object?)`, `TryGetStringProperty(IDictionary, string, out string?)`. Every `JsonValueKind` arm + case-insensitive lookup pinned in `Tests/.../Helpers/JsonValueCoercionTests.cs`.
- `vos.ManagedMicroservice.Delta/Helpers/EndpointSeedLoader.cs` — `Load(IEnumerable<string>, ILogger)` takes candidate paths as a parameter so tests use real temp files (cheap, no I/O mock); `LoadDefault(ILogger)` wraps with the canonical three paths.
- `vos.ManagedMicroservice.Metabolism/Helpers/JsonValueUnwrapper.cs` — `Unwrap(object?)` maps `JsonElement` to native CLR types with `int → long → decimal` width escalation.
- `vos.ManagedMicroservice.Tributary/Helpers/EffectivePropertyResolver.cs` — `TryGetEffectiveProperty` with exact-match-preempts-suffix precedence and a `conflicts` list for ambiguous suffixes.

What stays in `Program.cs`: DI registration, middleware order, route mapping, lifetime callbacks, endpoint lambdas with thin call-through bodies. Composition, not logic. The `coverage.runsettings` exclusion of `Program.cs` is honest after the extraction; before it, real testable code hid behind the exclusion.

### Program.cs (the same five endpoints + lifecycle)

Every microservice's `Program.cs`:
1. Parses CLI args; `Console.WriteLine(CliArgs.UsageMessage)` + `Environment.Exit(1)` on null.
2. Configures Serilog file logging under `logs/<service>-.log`.
3. Calls `WebApplication.CreateBuilder(args)`.
4. If `--signingKey` was supplied, calls `builder.AddBrokerTokenAuth(signingKey, issuer, audience)`.
5. Calls `builder.Services.AddContractValidation()` to register the schema registry + validator.
6. Registers a singleton `BrokerClient`.
7. Calls `app.UseRouting()`, then `app.UseRequestContractValidation()` (after auth if auth is enabled). The middleware reads `ContractValidationMetadata` off the matched endpoint, so it must run after `UseRouting` and before endpoint dispatch.
8. Maps **POST `/handle`**, **GET `/health`**, **GET `/stats`**, **POST `/shutdown`**. Each request DTO that has a JSON Schema is tagged `[ContractSchema("<$id>")]`; its route calls `.RequireContract<TRequest>()` to opt in to validation.
9. Wires `ApplicationStarted` to call `BrokerClient.RegisterAsync(port)` (best-effort; broker can also discover via `/health`).
10. Wires `ApplicationStopping` to call `BrokerClient.DeregisterAsync()`.
11. `app.Run()`.

The contract-validation wiring is the canonical reference in `vos.ManagedMicroservice.Metabolism/Program.cs` + `Endpoints/EndpointMapper.cs` (Feature #5426). Adopting it in a new microservice is three local edits: `AddContractValidation()`, `UseRequestContractValidation()`, and `.RequireContract<HandleRequest>()` on the route.

## Test-project shape

Every microservice has a sibling test project at `Tests/vos.ManagedMicroservice.<Name>.Tests/`. Mirrors the production project's reference graph + adds:
- `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`
- `FluentAssertions`, `Moq` (or NSubstitute — see `docs/TEST-STATE.md` "Watch items" #4)
- `Microsoft.AspNetCore.Mvc.Testing` if exercising endpoints via `WebApplicationFactory<Program>`
- Project references: the microservice + `vos.Tests.Shared`

Standard test files (one per testable unit):

```
Tests/vos.ManagedMicroservice.<Name>.Tests/
├── CliArgsTests.cs          ← CLI parser contract
├── BrokerClientTests.cs     ← service-specific RegisterAsync + inherited base behavior
└── <Service>Tests.cs        ← service-specific business logic (handle endpoint, etc.)
```

## Test patterns (worked examples in Echo)

### CliArgsTests

Pin the standard CLI contract that every microservice must satisfy. Tests are named `<Method>_<Scenario>_<Expected>_PerTemplate` so the template aspect is visible at a glance.

Required tests (see `Tests/vos.ManagedMicroservice.Echo.Tests/CliArgsTests.cs`):

| Test name | What it pins |
|---|---|
| `Parse_RequiredFlagsOnly_ReturnsArgsWithDefaultedOptionals_PerTemplate` | Required `--port` + `--brokerUrl` are sufficient; optionals default to `null` |
| `Parse_MissingPort_ReturnsNull_PerTemplate` | Required-arg validation fails closed |
| `Parse_MissingBrokerUrl_ReturnsNull_PerTemplate` | Required-arg validation fails closed |
| `Parse_InvalidPort_ReturnsNull_PerTemplate` (Theory) | Port out of `[1, 65535]` or non-numeric → null |
| `Parse_PortAtBoundaries_Accepted_PerTemplate` (Theory) | 1 and 65535 are valid |
| `Parse_AllOptionalFlags_PopulateRespectiveFields_PerTemplate` | Each optional flag round-trips |
| `Parse_FlagOrderIndependent_PerTemplate` | Args may appear in any order |
| `Parse_UnknownFlag_IgnoredSilently_PerTemplate` | Forward-compat: unknown flags don't crash |
| `UsageMessage_MentionsEverySupportedFlag_PerTemplate` | `--help`-style output stays in sync with `Parse` |

### BrokerClientTests

Pin the inherited contract (`HandlerId` uniqueness, `BrokerUrl` passthrough, `DeregisterAsync` sends DELETE, `GetTokenAsync` short-circuits with `--token`) plus the service-specific `RegisterAsync` body.

Standard helpers (use `vos.Tests.Shared.MockHttpMessageHandler` + `TestHttpClientFactory`):

```csharp
private static (BrokerClient client, MockHttpMessageHandler handler) NewClient(
    Func<HttpRequestMessage, HttpResponseMessage> respond,
    string? serviceToken = "svc-jwt-abc")
{
    var handler = new MockHttpMessageHandler(respond);
    var http = new HttpClient(handler);
    var factory = new TestHttpClientFactory(http);
    var client = new BrokerClient(factory, NullLogger<BrokerClient>.Instance, "http://localhost:7243", serviceToken);
    return (client, handler);
}
```

Required tests (see `Tests/vos.ManagedMicroservice.Echo.Tests/BrokerClientTests.cs`):

| Test name | What it pins |
|---|---|
| `HandlerId_IsUniquePerInstance_PerTemplate` | Two `BrokerClient` instances have distinct `HandlerId` Guids |
| `BrokerUrl_PassedThroughFromCtor_PerTemplate` | Constructor wires `BrokerUrl` |
| `RegisterAsync_Success_PostsServiceIdentity_PerTemplate` | POST body to `/api/broker/register` contains the service-specific `serviceName` + `startCommand` plus the standard `handlerId`/`endpointUrl`/`stopEndpoint`/`healthEndpoint` envelope |
| `RegisterAsync_BrokerReturnsFailure_ReturnsFalse_PerTemplate` | Non-2xx → false |
| `RegisterAsync_AuthFails_ReturnsFalse_PerTemplate` | `CreateAuthenticatedClient` throwing → caught → false |
| `DeregisterAsync_SendsDeleteToBroker_PerTemplate` | `DELETE /api/broker/services/{HandlerId}` with Bearer header |
| `GetTokenAsync_WithProvidedToken_ReturnsItDirectly_PerTemplate` | `--token` short-circuits the broker call |

### Service-specific endpoint tests

Echo has minimal business logic (echo body + counter) and per `coverage.runsettings` `Program.cs` is excluded from unit-test coverage — so Echo's test suite stops at `CliArgs` + `BrokerClient`.

Microservices with more substantial business logic (e.g. Metabolism's simulation engine, Tributary's `ObservationIngestService`) get a dedicated `<Service>Tests.cs` exercising that logic directly. When the endpoint surface needs unit-level coverage, use `WebApplicationFactory<Program>` from `Microsoft.AspNetCore.Mvc.Testing` and pass empty args + a Configure callback that injects the args via `WebApplicationFactoryClientOptions`. The `Program` class is `internal` by default with top-level statements — declare it `public partial class Program { }` at the bottom of `Program.cs` to make it accessible to the test factory.

## Coverage expectations

Phase 1 acceptance for each microservice test project: **≥95% line on `CliArgs` + `BrokerClient` + any `<Service>Tests.cs` business-logic class**. `Program.cs` is excluded by `coverage.runsettings` (integration-test territory; see CLAUDE.md note about minimal-API wireup).

> **Note:** the original wildcard `**/vos.ManagedMicroservice.*/Program.cs` was silently ignored because Phase 0 used nested `<File>` elements inside `<ExcludeByFile>` — coverlet's XPlat data collector expects a single comma-separated string. Fixed under Feature #5433 / Task #5435 with explicit per-microservice paths. New microservices need to add their own `Program.cs` to the comma-separated list in `coverage.runsettings`.

## Adding a new microservice

1. Copy `vos.ManagedMicroservice.Echo/` to `vos.ManagedMicroservice.<Name>/`. Rename the namespace, project file, and `BrokerClient`'s `serviceName`/`startCommand`.
2. Add the new project to `VillageOS-API.sln`.
3. Copy `Tests/vos.ManagedMicroservice.Echo.Tests/` to `Tests/vos.ManagedMicroservice.<Name>.Tests/`. Update the project reference + namespace; the test patterns transfer 1:1.
4. Add the test project to `VillageOS-API.sln`.
5. Run `dotnet test` from the repo root — the new project should be picked up automatically by the `**/*Tests.csproj` glob in `azure-pipelines.yml`.
6. Add a row to `docs/TEST-STATE.md` under "Where tests live".
