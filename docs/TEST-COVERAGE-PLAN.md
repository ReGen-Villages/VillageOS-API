# Test Coverage Plan — Drive .NET unit tests to a practical 95%+ baseline

Action plan derived from `docs/TEST-STATE.md`. This is the execution playbook for closing the unit-test coverage gaps; it is not a snapshot (the snapshot lives in `TEST-STATE.md`). Refresh `TEST-STATE.md` as phases land; this file can be deleted once every phase is delivered.

## Context

`docs/TEST-STATE.md` shows:

- **Three .NET projects at 0% direct unit-test coverage**: `vos.Core`, `vos.Application`, `vos.Infrastructure`. CLI tests mock the seam below the handler boundary, so the domain layer is not exercised at all.
- **One assembly with no test project**: `vos.Auth.Shared` (~150 LOC, untested directly).
- **One microservice with no test project**: `vos.ManagedMicroservice.Echo`.
- **Shared microservice base code covered only as a side effect**: `vos.Microservice.Shared` (~31% line coverage from incidental Metabolism/EndpointCaller/IntegrationRegistry tests).
- **Four partially-covered projects**: CLI ~72%, Metabolism ~51%, EndpointCaller ~23%, IntegrationRegistry ~19%.
- **No coverage threshold enforced** — `azure-pipelines.yml` collects Cobertura but does not gate merges; `CLAUDE.md` says "Coverage must improve or hold across every PR" but compliance is reviewer-enforced.

User decisions for this plan (2026-05-13):

- **Target**: practical **95%+ line coverage** with documented exclusions for minimal-API `Program.cs` wireup, generated code, and defensive-only branches that cannot be reached from a unit test.
- **Scope**: **.NET only**. GUI Vitest is out of scope; tracked separately in TEST-STATE.md Candidate follow-up #7.
- **Work-item structure**: **one umbrella AzDO Feature + one child Task per project**.
- **TDD adoption**: saved as auto-memory and documented in `CLAUDE.md`.

End-state: every .NET assembly has a dedicated test project, the four currently-untested assemblies are exercised directly (not just mocked at the seam), `coverage.runsettings` enforces a per-assembly minimum, the AzDO pipeline fails when coverage drops, and TDD is the documented working convention for this repo.

## Approach

Build outward from the foundations: shared test infra → new test projects for the 0%-coverage assemblies → expansion of existing partial projects → coverage-gate enforcement → TDD documentation. This ordering means earlier tasks unblock later ones (shared `MockHttpMessageHandler` lands before the new microservice test projects need it), and the coverage gate is wired up only once there's something worth gating against.

The TEST-STATE.md test-case backlog (lines 86–161) is the seed for the new test projects — those names map directly to files to be created. New tests beyond that backlog will be discovered as production code is read; the per-task acceptance criterion is the 95% threshold, **not** the bullet list.

## Phase 0 — Foundation (one Task)

**Goal:** scaffold AzDO items, shared test helpers, and coverage settings so subsequent tasks are pure execution.

- Create AzDO Feature: **"Unit test coverage to practical 95%+ baseline (.NET)"** under the existing `VillageOS-API` project. Use `New-AzDoWorkItem -Type Feature` from `Tools/AzDO.psm1`. Assign all subsequent tasks as children via `-ParentId`.
- Create `Tests/vos.Tests.Shared/` class library (`net10.0`) holding the canonical `MockHttpMessageHandler` and `TestHttpClientFactory`. Migrate the three duplicated copies (`Tests/vos.ManagedMicroservice.{Metabolism,EndpointCaller,IntegrationRegistry}.Tests/MockHttpMessageHandler.cs`) to reference the shared project and delete the duplicates. **Decision deferred**: mocking-library convergence (Moq vs NSubstitute) — flag in the README of the new shared project, do not converge in this Feature.
- Update `coverage.runsettings`: add `<CodeCoverage>` config with `<ModulePaths><Exclude>` for `*Tests*.dll`, source-file exclusion for `**/Program.cs` in microservice projects (Program.cs is replaced by integration-test scope, not unit-test scope), and function-name exclusion for compiler-generated `<*>b__*` closures. Document each exclusion with a comment explaining why it's there so reviewers understand.

## Phase 1 — New test projects for 0%-coverage assemblies (one Task per project, six total)

Each task creates a new `Tests/<assembly>.Tests/` project using `vos.CLI.Tests/vos.CLI.Tests.csproj` as the template (xUnit 2.7.0, FluentAssertions 6.12.0, Moq 4.20.70, coverlet.collector 6.0.0, `net10.0`). Each task gets its own AzDO Test Case work item per CLAUDE.md, then the test code is written **TDD-style** — failing tests first, implementation already exists so coverage moves green incrementally.

| Task | New project | Sized at | Backlog seeds |
|---|---|---|---|
| 1A | `Tests/vos.Core.Tests/` | 80–120 tests | TEST-STATE.md lines 89–102 (`VosModel`, `VosRelationship`, `ExpectedRange`, `StateIndex`). Add `VosObject`, `VosProperty` (4 PropertyMode paths + RingBuffer wraparound), `CriteriaParser`, `DependencyGraph`, `RangeEvaluationService` (cycle detection). |
| 1B | `Tests/vos.Application.Tests/` | 30–50 tests | TEST-STATE.md lines 104–113 (`VosRelationshipService`, `VosThingService`). Add `VosObjectService`, `VosModelService`, `SurfaceThingClassifier`, JSON serialization helpers. |
| 1C | `Tests/vos.Infrastructure.Tests/` | 10–20 tests | TEST-STATE.md lines 115–124 (`ModelStore` concurrency, `VosModelProvider.FromJson`, properties/ranges/bindings parsing). |
| 1D | `Tests/vos.Auth.Shared.Tests/` | 5–10 tests | `ServiceTokenValidator` parameter shape (clock skew, signing key, audience), `HandlerAuthExtensions.AddBrokerTokenAuth` DI registration, `VosClaims`/`VosRoles`/`VosPolicies` constants — keep light, the assembly is 150 LOC. |
| 1E | `Tests/vos.ManagedMicroservice.Echo.Tests/` | 15–25 tests | TEST-STATE.md lines 126–136 (`CliArgs`, `BrokerClient`, `/echo`, `/health`, `/stats`, `/shutdown`). Mirror `Tests/vos.ManagedMicroservice.Metabolism.Tests/` shape — same `MockHttpMessageHandler` (now from shared project). |
| 1F | `Tests/vos.Microservice.Shared.Tests/` | 10–15 tests | TEST-STATE.md lines 153–161 (`BrokerClientBase` token flow, register/deregister, HandlerId uniqueness). Plus `HttpMethodValidator.IsSupportedMethod` and `RequiredPropertyValidator.GetMissingRequiredKeys`. |

Each project must be added to `VillageOS-API.sln` via `dotnet sln add`.

## Phase 2 — Expand partial-coverage projects to ≥95% (one Task per project, four total)

| Task | Project | Sized work |
|---|---|---|
| 2A | `vos.CLI.Tests/` (~72% → ≥95%) | Add `BrokerStatusCommandHandlerTests.cs`, `UserCommandHandlerTests.cs`. Audit the existing 22 test files for branch gaps (FluentAssertions `.Should().Throw<>()` patterns already in use). |
| 2B | `Tests/vos.ManagedMicroservice.Metabolism.Tests/` (~51% → ≥95%) | Add `EndpointMapperTests.cs` for `/handle`, `/simulations` GET/DELETE, `/health`, `/stats`, `/shutdown` using `WebApplicationFactory<Program>`. Add `SimulationEntry` direct tests (currently exercised only indirectly). |
| 2C | `Tests/vos.ManagedMicroservice.EndpointCaller.Tests/` (~23% → ≥95%) | TEST-STATE.md lines 138–152: `CallEndpointAsync` (GET/POST/method validation), `TryGetEffectiveProperty` conflict handling, `/handle` endpoint URL/URI validation and JSONata response transform, `ObservationIngestService` parsing paths. |
| 2D | `Tests/vos.ManagedMicroservice.IntegrationRegistry.Tests/` (~19% → ≥95%) | `/handle` and `/register` endpoint logic, `seed.json` parsing, property validation via `RequiredPropertyValidator`, conflict detection, compensation logic in `Program.cs`. |

## Phase 3 — Coverage gate enforcement (one Task) — **DELIVERED** (PR open against Task #5406)

Final shape diverged from the original sketch — recorded here so the plan reflects what shipped:

- **Per-assembly thresholds live in `Tools/Test-CoverageGate.ps1`**, not in `coverage.runsettings`. Coverlet's `<Threshold>` element only supports a single global value, not per-assembly. The standalone script merges per-project Cobertura XMLs via `reportgenerator` (already installed in CI), parses `<package>` elements, and compares line + branch rates against an inline hashtable. Same script runs locally and in CI — no "what does the YAML do that I can't reproduce" gap.
- **Gate shape: per-assembly no-regression.** Each threshold set at its develop-tip value rounded down (so a sub-1pp flake doesn't trip CI, a real regression does). Per-class enforcement + aggressive `[ExcludeFromCodeCoverage]` annotations were considered and deferred — see `docs/TEST-STATE.md` > *Coverage gate (Phase 3 / Task #5406)* for the design tradeoffs.
- **Branch coverage included** in the gate (originally suggested by the plan at 85%; landed at each assembly's actual current branch rate rounded down — Metabolism's 81% being the lowest).
- `azure-pipelines.yml` gains one `PowerShell@2` step pointing at the script + a `PublishBuildArtifacts` step that uploads the merged HTML report as a pipeline artifact.
- CLAUDE.md "CI" section updated to remove the "no threshold is enforced" caveat and describe the gate + how to raise thresholds.

## Phase 4 — TDD documentation + memory (one Task)

- Add a **"Test-driven development"** section to `CLAUDE.md` (place after the "Before touching code" subsection of "Workflow"). Cover: red-green-refactor cycle, write the failing test before any production change (not just bug fixes — already the rule for bugs), commit the failing test or stash before going green so the regression history is visible, the 95% gate is the floor not the goal.
- Mirror the new section into the AzDO and GitHub wikis using the CLAUDE.md "Updating the DevOps wiki" pattern.
- **Save user feedback memory**: `feedback_tdd.md` with rule **"Always use TDD in VillageOS-API"**, **Why**: "user explicitly directed it on 2026-05-13", **How to apply**: "write failing test first for any code change in this repo, including non-bug work — only existing exception is single-line typo fixes". Add a one-line entry to `MEMORY.md`. This save was deliberately deferred to this phase because plan mode blocked writes when the rule was given.

## Critical files

**To create:**

- `Tests/vos.Tests.Shared/vos.Tests.Shared.csproj` + `MockHttpMessageHandler.cs` + `TestHttpClientFactory.cs`
- `Tests/vos.Core.Tests/`, `Tests/vos.Application.Tests/`, `Tests/vos.Infrastructure.Tests/`, `Tests/vos.Auth.Shared.Tests/`, `Tests/vos.ManagedMicroservice.Echo.Tests/`, `Tests/vos.Microservice.Shared.Tests/` — each with `.csproj` + test files

**To modify:**

- `VillageOS-API.sln` — add the seven new projects
- `coverage.runsettings` — add exclusions and per-assembly minimums (currently minimal — only declares the `XPlat Code Coverage` data collector)
- `azure-pipelines.yml` — add `reportgenerator` + coverage-gate step
- `CLAUDE.md` — add TDD section under Workflow; remove "No threshold is enforced" from the CI section
- `docs/TEST-STATE.md` — refresh the snapshot table once Phase 1 + 2 complete; remove items from Candidate follow-ups as they're delivered
- `vos.CLI.Tests/`, `Tests/vos.ManagedMicroservice.{Metabolism,EndpointCaller,IntegrationRegistry}.Tests/` — replace local `MockHttpMessageHandler.cs` with project ref to `vos.Tests.Shared`; add new test files per Phase 2

**Reusable utilities to lean on (don't reinvent):**

- `vos.CLI.Tests/MockHttpMessageHandler` pattern (or whichever is selected as canonical) — moved into `vos.Tests.Shared`
- `Tests/vos.ManagedMicroservice.EndpointCaller.Tests/TestHttpClientFactory.cs` — moved into `vos.Tests.Shared`
- AzDO toolkit at `Tools/AzDO.psm1` for all work-item creation (`New-AzDoWorkItem -Type Feature/Task`, `Set-AzDoPrWorkItemLink`)
- `vos.Microservice.Shared.Validation.HttpMethodValidator` and `RequiredPropertyValidator` — already exist; tests will exercise them directly via Phase 1F

## Sequencing & parallelism

- Phase 0 must land first.
- Phase 1 tasks (A–F) are independent of each other and can be developed in parallel by different sessions/branches.
- Phase 2 tasks (A–D) are independent of each other and of Phase 1.
- Phase 3 cannot land until at least one of Phase 1/2 hits 95% (otherwise the gate fails immediately). Land Phase 3 after the easiest project (likely 1D `vos.Auth.Shared.Tests/` or 1F `vos.Microservice.Shared.Tests/`) to validate the gate config end-to-end, then progressively widen scope.
- Phase 4 can be done at any time — it's docs + memory.

## Verification

After each phase:

```powershell
# Full solution build + test run with coverage
dotnet test --configuration Release `
  --collect:"XPlat Code Coverage" --settings coverage.runsettings `
  --results-directory TestResults\coverage-run `
  --logger "console;verbosity=minimal"

# Generate human-readable report
reportgenerator `
  -reports:TestResults\coverage-run\**\coverage.cobertura.xml `
  -targetdir:TestResults\coverage-report `
  -reporttypes:Html;TextSummary

# Read TextSummary to confirm per-assembly numbers vs the 95% target
Get-Content TestResults\coverage-report\Summary.txt
```

After Phase 3 the pipeline gate provides the same signal automatically — a PR that drops coverage will fail CI.

End-to-end TDD verification (Phase 4): on the next code change in this repo, the failing-test-first pattern is followed without prompting; the user can confirm by inspecting commit order on the next PR (test commit precedes implementation commit, or both are in one commit with the test written first per dev log).

## Open items / risks

- **Mocking library convergence (Moq vs NSubstitute)** — explicitly **out of scope** of this Feature. Tracked in TEST-STATE.md "Watch items" #4. Will become an issue if the same test needs to mock something across libraries; until then, defer.
- **Program.cs coverage in minimal-API projects** — excluded from unit-test scope per the 95%-with-exclusions decision. If integration-test coverage is desired separately, that's a follow-up Feature, not this one.
- **GUI Vitest** — out of scope per the user's decision; tracked separately in TEST-STATE.md Candidate follow-up #7.
- **`vos.Core` size** — 3,800 LOC and the `RangeEvaluationService` cycle-detection logic is the highest-complexity surface in the codebase. Phase 1A (Task 1A) is the single longest task in this Feature; consider breaking it into sub-tasks (e.g., `vos.Core.Tests/Domain` and `vos.Core.Tests/ExpectedValues`) once a session starts on it.
