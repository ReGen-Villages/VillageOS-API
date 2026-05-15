# Rename runbook — `IntegrationRegistry` → `Delta`, `EndpointCaller` → `Tributary`

Status: **planned, not executed**. Execute the steps below in order. Each step is a discrete change; commit after each major section if you want bisect-friendly history, or as one atomic commit at the end.

## Pre-flight

- **Work-item number required before commit.** Either link an existing AzDO item, or create a new Task:
  ```powershell
  Import-Module .\Tools\AzDO.psm1
  New-AzDoWorkItem -Type Task -Title 'Rename IntegrationRegistry→Delta, EndpointCaller→Tributary' -Description 'Apply river-ecosystem naming aesthetic to two ManagedMicroservices. No behavioral changes; rename only. Broker companion PR pending in ReGenVillages/VillageOS.'
  ```
  Capture the returned `id` as `$workItemId` — used in the commit subject and `Set-AzDoPrWorkItemLink` at PR time.
- **Broker side is out of scope.** Broker-managed launches of these two services will fail until the companion PR lands in `ReGenVillages/VillageOS`. Document this in the PR description.
- **No backwards-compatibility shims.** Per CLAUDE.md "Pre-release, no compatibility shims": delete the old strings entirely, do not keep aliases.

## Naming map

| Old | New |
|---|---|
| `vos.ManagedMicroservice.IntegrationRegistry` (folder, project, namespace, assembly) | `vos.ManagedMicroservice.Delta` |
| `vos.ManagedMicroservice.IntegrationRegistry.Tests` | `vos.ManagedMicroservice.Delta.Tests` |
| `vos.ManagedMicroservice.EndpointCaller` | `vos.ManagedMicroservice.Tributary` |
| `vos.ManagedMicroservice.EndpointCaller.Tests` | `vos.ManagedMicroservice.Tributary.Tests` |
| `/health` `service = "EndpointCaller"` / `"IntegrationRegistry-CSharp"` | `"Tributary"` / `"Delta"` |
| Serilog `Service` property | `"Tributary"` / `"Delta"` |
| Log prefix `endpointcaller-.log` / `integrationregistry-.log` | `tributary-.log` / `delta-.log` |
| Log strings ("Shutting down …", "… terminated unexpectedly", "VillageOS … Service") | `Tributary` / `Delta` |
| launchSettings profile names | match new project names |

Class names (`BrokerClient`, `CliArgs`, `ObservationIngestService`, `RegisterEndpointRequest`, `EndpointCallRequest`, `IEndpointBrokerClient`) **do not contain the service name** — they stay as-is. Ports stay (IntegrationRegistry/Delta: 7247; EndpointCaller/Tributary debug: 54643/54644).

---

## Step A — `IntegrationRegistry` → `Delta`

### A1. Move folders (preserves git history)
```powershell
git mv vos.ManagedMicroservice.IntegrationRegistry vos.ManagedMicroservice.Delta
git mv Tests\vos.ManagedMicroservice.IntegrationRegistry.Tests Tests\vos.ManagedMicroservice.Delta.Tests
```

### A2. Rename `.csproj` files
```powershell
git mv vos.ManagedMicroservice.Delta\vos.ManagedMicroservice.IntegrationRegistry.csproj vos.ManagedMicroservice.Delta\vos.ManagedMicroservice.Delta.csproj
git mv Tests\vos.ManagedMicroservice.Delta.Tests\vos.ManagedMicroservice.IntegrationRegistry.Tests.csproj Tests\vos.ManagedMicroservice.Delta.Tests\vos.ManagedMicroservice.Delta.Tests.csproj
```

### A3. Inside each renamed `.csproj`
- Check for `<AssemblyName>` and `<RootNamespace>` elements. If present and they spell out the old name, update to `vos.ManagedMicroservice.Delta` / `.Tests`.
- Inside the **test** csproj, the `<ProjectReference Include="...vos.ManagedMicroservice.IntegrationRegistry.csproj" />` needs to point to the renamed main csproj.

### A4. Update `VillageOS-API.sln`
Two `Project(…)` declaration lines (approx. lines 10 and 28 of the inventory):
- Replace the project name string, the relative path, **but keep the project GUIDs** (the GUIDs are referenced again in `GlobalSection(ProjectConfigurationPlatforms)` — don't break those).
- After editing, run `dotnet sln list` and confirm the two new names appear.

### A5. Namespace + using rewrites
Files to edit (all inside the renamed folders):
- `vos.ManagedMicroservice.Delta\Program.cs`
- `vos.ManagedMicroservice.Delta\Configuration\CliArgs.cs`
- `vos.ManagedMicroservice.Delta\Models\RegisterEndpointRequest.cs`
- `vos.ManagedMicroservice.Delta\Services\BrokerClient.cs`
- `Tests\vos.ManagedMicroservice.Delta.Tests\CliArgsTests.cs`
- `Tests\vos.ManagedMicroservice.Delta.Tests\MockHttpMessageHandler.cs`
- `Tests\vos.ManagedMicroservice.Delta.Tests\BrokerClientTests.cs`
- (Any other `.cs` files added since the inventory — recheck with `Grep`)

Replace every occurrence of `vos.ManagedMicroservice.IntegrationRegistry` with `vos.ManagedMicroservice.Delta` (covers both `namespace` declarations and `using` directives).

### A6. Identity-string changes in `Program.cs`
Per inventory, in `vos.ManagedMicroservice.Delta\Program.cs`:
- Line ~23: log path prefix `"integrationregistry-.log"` → `"delta-.log"`
- Line ~28: `WithProperty("Service", "IntegrationRegistry")` → `"Delta"`
- Line ~38: `"VillageOS IntegrationRegistry Service"` → `"VillageOS Delta Service"`
- Line ~83: `/health` returns `"IntegrationRegistry-CSharp"` → `"Delta"` (drop the `-CSharp` suffix)
- Line ~93: `"Shutting down IntegrationRegistry"` → `"Shutting down Delta"`
- Line ~101: `"IntegrationRegistry terminated unexpectedly"` → `"Delta terminated unexpectedly"`

### A7. launchSettings profile rename
In `vos.ManagedMicroservice.Delta\Properties\launchSettings.json`, change the profile key `"vos.ManagedMicroservice.IntegrationRegistry"` to `"vos.ManagedMicroservice.Delta"`. Port `7247` stays.

### A8. Shared XML doc comment
In `vos.Microservice.Shared\BrokerClientBase.cs` (~line 11), the XML doc mentions "IntegrationRegistry handler services" — update to "Delta handler services" or generalise.

---

## Step B — `EndpointCaller` → `Tributary`

Same shape as Step A, parameterised:

### B1. Move folders
```powershell
git mv vos.ManagedMicroservice.EndpointCaller vos.ManagedMicroservice.Tributary
git mv Tests\vos.ManagedMicroservice.EndpointCaller.Tests Tests\vos.ManagedMicroservice.Tributary.Tests
```

### B2. Rename `.csproj` files
```powershell
git mv vos.ManagedMicroservice.Tributary\vos.ManagedMicroservice.EndpointCaller.csproj vos.ManagedMicroservice.Tributary\vos.ManagedMicroservice.Tributary.csproj
git mv Tests\vos.ManagedMicroservice.Tributary.Tests\vos.ManagedMicroservice.EndpointCaller.Tests.csproj Tests\vos.ManagedMicroservice.Tributary.Tests\vos.ManagedMicroservice.Tributary.Tests.csproj
```

### B3–B4. Same as A3–A4 (csproj internals + `.sln`).

### B5. Namespace + using rewrites
Files:
- `vos.ManagedMicroservice.Tributary\Program.cs`
- `vos.ManagedMicroservice.Tributary\Configuration\CliArgs.cs`
- `vos.ManagedMicroservice.Tributary\Models\EndpointCallRequest.cs`
- `vos.ManagedMicroservice.Tributary\Services\BrokerClient.cs`
- `vos.ManagedMicroservice.Tributary\Services\IEndpointBrokerClient.cs`
- `vos.ManagedMicroservice.Tributary\Services\ObservationIngestService.cs`
- `Tests\vos.ManagedMicroservice.Tributary.Tests\CliArgsTests.cs`
- `Tests\vos.ManagedMicroservice.Tributary.Tests\MockHttpMessageHandler.cs`
- `Tests\vos.ManagedMicroservice.Tributary.Tests\ObservationIngestServiceTests.cs`
- `Tests\vos.ManagedMicroservice.Tributary.Tests\TestHttpClientFactory.cs`
- `Tests\vos.ManagedMicroservice.Tributary.Tests\BrokerClientTests.cs`

Replace every occurrence of `vos.ManagedMicroservice.EndpointCaller` with `vos.ManagedMicroservice.Tributary`.

### B6. Identity-string changes in `Program.cs`
Per inventory, in `vos.ManagedMicroservice.Tributary\Program.cs`:
- Line ~25: log path prefix `"endpointcaller-.log"` → `"tributary-.log"`
- Line ~30: `WithProperty("Service", "EndpointCaller")` → `"Tributary"`
- Line ~257: `/health` response `service = "EndpointCaller"` → `"Tributary"`
- Line ~267: shutdown response `"Shutting down EndpointCaller"` → `"Shutting down Tributary"`
- Line ~275: fatal log `"EndpointCaller terminated unexpectedly"` → `"Tributary terminated unexpectedly"`

### B7. launchSettings profile rename
In `vos.ManagedMicroservice.Tributary\Properties\launchSettings.json`, change the profile key. Debug ports `54643`/`54644` stay.

---

## Step C — Documentation sweep

Per CLAUDE.md "Before `git commit`" → README + docs + DevOps wiki + GitHub wiki sweeps.

### C1. In-repo docs
- `README.md` — microservices table: rename the two rows.
- `CLAUDE.md` — the "## Microservice architecture" section lists `(Echo, EndpointCaller, IntegrationRegistry, Metabolism)`. Change to `(Echo, Tributary, Delta, Metabolism)`. **Note:** after Step D, `CLAUDE.md` is gitignored — this edit stays local but should still be applied for consistency.
- `docs/DELIVERY.md` — 13 IntegrationRegistry mentions + multiple EndpointCaller mentions across migration phases, examples, comparisons. Read the file, rewrite each in context (don't blindly find/replace — some sentences need rephrasing when the name flips).
- `docs/TEST-STATE.md` — test-project rows and coverage table.
- `docs/METABOLISM.md` — verify no cross-references survive (inventory says none — confirm with `Grep`).
- `docs/AZDO-TOOLKIT.md` — verify (inventory says none).
- Per-service `README.md` inside each renamed folder, if present (check during execution).

### C2. DevOps wiki
For every `docs/*.md` you edited, update the mirrored wiki page. Use the `Invoke-RestMethod` pattern in CLAUDE.md "Updating the DevOps wiki" (PAT + `If-Match` ETag). Pages to check/update:
- `Delivery` (mirror of `docs/DELIVERY.md`)
- `Test-State` (mirror of `docs/TEST-STATE.md`)
- Any wiki page named after either old service (`IntegrationRegistry`, `EndpointCaller`) — rename or replace.

### C3. GitHub wiki
Independent content from the DevOps wiki. Update equivalent pages on `github.com/regenrob/VillageOS-API/wiki`.

---

## Step D — `.gitignore` tweak

Append `CLAUDE.md` to `.gitignore`. The file is currently untracked (per session-start `git status`: `?? CLAUDE.md`), so no `git rm --cached` needed.

```powershell
Add-Content .gitignore "`nCLAUDE.md`n"
```

---

## Step E — Dead-code sweep

After A–D, grep the entire repo (case-insensitive) for survivors:

```powershell
# Should return zero hits (except possibly under .git/, bin/, obj/).
# Note: rg/grep are discouraged in the harness — use the Grep tool when running this from Claude.
```

Patterns to check: `IntegrationRegistry`, `EndpointCaller`, `integrationregistry`, `endpointcaller`, `integration-registry`, `endpoint-caller`.

Any hit outside `bin/`/`obj/`/`.git/`/`logs/` is a missed rename — fix it before continuing.

---

## Step F — Build + test gate

```powershell
dotnet restore
dotnet build
dotnet test
```

All projects must build; the entire test suite must be green (CLAUDE.md: "Coverage must improve or hold across every PR. Never ship a coverage drop." — for a pure rename, coverage holds by construction, but confirm).

---

## Step G — Smoke test

Launch each renamed service briefly and confirm identity strings:

```powershell
# Delta
dotnet run --project vos.ManagedMicroservice.Delta -- --port=7247 --brokerUrl=https://localhost:7243
# In another window:
curl https://localhost:7247/health   # expect { service: "Delta", ... }
ls logs\delta-*.log                  # expect a file to exist

# Tributary
dotnet run --project vos.ManagedMicroservice.Tributary -- --port=7248 --brokerUrl=https://localhost:7243
curl https://localhost:7248/health   # expect { service: "Tributary", ... }
ls logs\tributary-*.log
```

(Tributary's normal port isn't fixed in launchSettings — pick any free port for the smoke test.)

---

## Step H — Commit + PR

```powershell
git add -A   # confirm via git status that only intended files are staged
git commit -m @"
Task #$workItemId: rename IntegrationRegistry→Delta, EndpointCaller→Tributary

Apply river-ecosystem naming aesthetic to two ManagedMicroservices.
No behavioral changes; rename of folders, projects, namespaces, and
identity strings (service property, /health payload, log prefixes).
Broker companion PR pending in ReGenVillages/VillageOS.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
"@
```

Push and open the PR. Then link the PR to the work item:

```powershell
Set-AzDoPrWorkItemLink -WorkItemId $workItemId -PullRequestId <prId> -RepositoryId <repoGuid> -ProjectId <projectGuid>
```

PR description checklist:
- Summary of the rename and the metaphor justification.
- **Explicit note: broker companion PR pending** — broker-managed launches of these two services will fail until the companion PR lands.
- Confirmation that all wiki pages (DevOps + GitHub) have been updated.

---

## Rollback

Pre-merge: `git reset --hard <pre-rename-sha>`. Post-merge: revert the PR (one revert commit). The broker repo isn't touched by this PR, so there's no cross-repo coordination needed for a rollback.
