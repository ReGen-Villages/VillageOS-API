# vos.Tests.Shared

Cross-project test helpers used by every `Tests/*.Tests/` project. Class library, no test runner — only types meant to be consumed by other test projects live here.

## What's in here

- `MockHttpMessageHandler` — `HttpMessageHandler` that delegates to a user-supplied lambda and records every inbound `HttpRequestMessage` in `Requests`. Use it to fake broker HTTP from microservice tests.
- `TestHttpClientFactory` — `IHttpClientFactory` that always returns the single `HttpClient` it was constructed with. Pair with `MockHttpMessageHandler` to inject a stubbed pipeline through DI.

## Deferred decision: mocking library convergence

The microservice test projects currently mix Moq and NSubstitute (Moq in CLI/Metabolism, NSubstitute in Tributary/Delta). Converging on one is out of scope for the test-coverage Feature (#5394) — tracked in `docs/TEST-STATE.md` Watch items #4.

Until then, **do not add a mocking-library `PackageReference` to this project**. The shared helpers are pure handcrafted fakes so they remain library-agnostic and can be consumed by either side.
