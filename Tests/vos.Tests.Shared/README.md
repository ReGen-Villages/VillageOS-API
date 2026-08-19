# vos.Tests.Shared

Cross-project test helpers used by every `Tests/*.Tests/` project. Class library, no test runner — only types meant to be consumed by other test projects live here.

## What's in here

- `MockHttpMessageHandler` — `HttpMessageHandler` that delegates to a user-supplied lambda and records every inbound `HttpRequestMessage` in `Requests`. Use it to fake Mycelium HTTP from microservice tests.
- `RecordingHttpMessageHandler` — the same, but recording each request's method, address and body as text rather than keeping the live message. Use it when a test asserts on the **value** a service wrote: a request's content is disposed with the request, so a body read after the call has finished is empty.
- `TestHttpClientFactory` — `IHttpClientFactory` that always returns the single `HttpClient` it was constructed with. Pair with `MockHttpMessageHandler` to inject a stubbed pipeline through DI.
- `TestCulture` — runs a body under a named regional format so a test does not inherit the machine's. Two distinct uses:
  - `TestCulture.CommaDecimal` proves that **reading** a value ignores the regional format. Under `nl-NL` the string `"30.5"` parses as `305` unless the invariant culture is used — the dot reads as a thousands separator and no error is raised. A test that does not force this passes on an en-US build agent and only fails on a machine already set to a comma-decimal region.
  - `TestCulture.Display` pins output that **is** meant to follow the operator's region, so an assertion like `"4.0%"` names the culture it expects. It is `en-US` rather than the invariant culture, whose percent pattern inserts a space before the sign (`"4.0 %"`).
- `DeclaredOutputs.Of<THandler>()` — the property names a compute handler writes, read off the handler's public string constants. Use it wherever a test needs the whole output set: a list restated in the test stays one short the day an output is added, and the assertion it was making quietly stops covering the new one.
- `EffectiveProperties.Carrying(names)` — a study's effective properties in the shape the broker answers with, every value set to one. Use it when the test is about **which** properties a handler reads rather than what it computes from them.

## Deferred decision: mocking library convergence

The microservice test projects currently mix Moq and NSubstitute (Moq in CLI/Metabolism, NSubstitute in Tributary/Delta). Converging on one is out of scope for the current test-coverage work and remains a deferred decision.

Until then, **do not add a mocking-library `PackageReference` to this project**. The shared helpers are pure handcrafted fakes so they remain library-agnostic and can be consumed by either side.
