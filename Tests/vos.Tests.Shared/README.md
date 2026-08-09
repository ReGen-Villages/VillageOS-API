# vos.Tests.Shared

Cross-project test helpers used by every `Tests/*.Tests/` project. Class library, no test runner — only types meant to be consumed by other test projects live here.

## What's in here

- `MockHttpMessageHandler` — `HttpMessageHandler` that delegates to a user-supplied lambda and records every inbound `HttpRequestMessage` in `Requests`. Use it to fake Mycelium HTTP from microservice tests.
- `TestHttpClientFactory` — `IHttpClientFactory` that always returns the single `HttpClient` it was constructed with. Pair with `MockHttpMessageHandler` to inject a stubbed pipeline through DI.
- `TestCulture` — runs a body under a named regional format so a test does not inherit the machine's. Two distinct uses:
  - `TestCulture.CommaDecimal` proves that **reading** a value ignores the regional format. Under `nl-NL` the string `"30.5"` parses as `305` unless the invariant culture is used — the dot reads as a thousands separator and no error is raised. A test that does not force this passes on an en-US build agent and only fails on a machine already set to a comma-decimal region.
  - `TestCulture.Display` pins output that **is** meant to follow the operator's region, so an assertion like `"4.0%"` names the culture it expects. It is `en-US` rather than the invariant culture, whose percent pattern inserts a space before the sign (`"4.0 %"`).

## Deferred decision: mocking library convergence

The microservice test projects currently mix Moq and NSubstitute (Moq in CLI/Metabolism, NSubstitute in Tributary/Delta). Converging on one is out of scope for the current test-coverage work and remains a deferred decision.

Until then, **do not add a mocking-library `PackageReference` to this project**. The shared helpers are pure handcrafted fakes so they remain library-agnostic and can be consumed by either side.
