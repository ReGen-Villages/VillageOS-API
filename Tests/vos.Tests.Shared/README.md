# vos.Tests.Shared

Cross-project test helpers used by every `Tests/*.Tests/` project. Class library, no test runner — only types meant to be consumed by other test projects live here.

## What's in here

- `RepositoryRoot.Find()` — the checkout the test is running from, found by walking up to the solution file. Combine a path onto it to reach a file in source. Every test that reads repository text goes through this, so no test searches upward for a file itself and walks out of its own worktree into the checkout above.
- `MockHttpMessageHandler` — `HttpMessageHandler` that delegates to a user-supplied lambda and records every inbound `HttpRequestMessage` in `Requests`. Use it to fake Mycelium HTTP from microservice tests.
- `RecordingHttpMessageHandler` — the same, but recording each request's method, address and body as text rather than keeping the live message. Use it when a test asserts on the **value** a service wrote: a request's content is disposed with the request, so a body read after the call has finished is empty.
- `TestHttpClientFactory` — `IHttpClientFactory` that always returns the single `HttpClient` it was constructed with. Pair with `MockHttpMessageHandler` to inject a stubbed pipeline through DI.
- `PerCallHttpClientFactory` — `IHttpClientFactory` that hands out a fresh client per call, the way the real factory does. Use it when the subject makes more than one call: a client's timeout cannot be set again once a request is in flight, so a single shared client fails the second call.
- `MyceliumSigner` — stands in for Mycelium in a handler's tests. It holds a key pair on the P-256 curve, hands out the public half as the `VerificationKey` setting a daemon is launched with, and signs the tokens a handler is meant to accept and the ones it must refuse.
- `TestTokens` — unsigned tokens shaped like the ones Mycelium mints, for a test of a service reading its own credential to learn which project it speaks for and when to replace it.
- `CapturingLogger<T>` — keeps what a component logged, so a test can say what a line must and must not carry. Register it as the closed `ILogger<T>`: Serilog owns a host's logging and drops providers added through `ConfigureLogging`.
- `TestCulture` — runs a body under a named regional format so a test does not inherit the machine's. Two distinct uses:
  - `TestCulture.CommaDecimal` proves that **reading** a value ignores the regional format. Under `nl-NL` the string `"30.5"` parses as `305` unless the invariant culture is used — the dot reads as a thousands separator and no error is raised. A test that does not force this passes on an en-US build agent and only fails on a machine already set to a comma-decimal region.
  - `TestCulture.Display` pins output that **is** meant to follow the operator's region, so an assertion like `"4.0%"` names the culture it expects. It is `en-US` rather than the invariant culture, whose percent pattern inserts a space before the sign (`"4.0 %"`).
- `Settle` — the one clock a test may put on a wait for something to happen. A fixed wait is an assertion about how busy the build agent is, so it passes on an idle machine and reddens an unrelated pull request on a loaded one; `Settle` waits as long as the work actually takes and gives up only after a ceiling long enough that reaching it means a genuine hang. `TestWaitsKeepTheSharedCeilingTests` in `Tests/vos.ContinuousIntegration.Tests` fails a test file that writes any other clock.
  - `Settle.UntilAsync(condition, expectation)` waits for something a test cannot await: a background loop's effect, a fire-and-forget write. Use it wherever the alternative is `await Task.Delay(someNumber)`.
  - `Settle.ForAsync(task, expectation)` waits for a task.
  - `Settle.Ceiling` is the duration itself, for a wait that needs a clock of its own.
  - `Settle.BeforeAssertingAbsenceAsync(window)` is the pause before asserting that something did **not** happen, and the only short wait a test keeps: a machine too busy to get to it lets such a test pass without proof, and can never make it fail.
- `DeclaredOutputs.Of<THandler>()` — the property names a compute handler writes, read off the handler's public string constants. Use it wherever a test needs the whole output set: a list restated in the test stays one short the day an output is added, and the assertion it was making quietly stops covering the new one.
- `EffectiveProperties.Carrying(names)` — a study's effective properties in the shape the broker answers with, every value set to one. Use it when the test is about **which** properties a handler reads rather than what it computes from them.

## No mocking library here

The test projects use two mocking libraries: Moq in some, NSubstitute in others. **Do not add a mocking-library `PackageReference` to this project.** The shared helpers are hand-written fakes, so a test project on either library can consume them.
