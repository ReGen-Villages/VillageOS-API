# AzDO Toolkit

## What It Is

`Tools/AzDO.psm1` is a PowerShell module that talks to Azure DevOps via the REST API. It's how Claude/scripts in this repo create and update work items, post discussion comments, link PRs to items, and (eventually) update wiki pages. It uses a PAT (`$env:AZDO_PAT`) with Basic auth — no Azure CLI required.

Exported functions: `Get-AzDoCurrentUser`, `Get-AzDoWorkItem`, `New-AzDoWorkItem`, `Update-AzDoWorkItem`, `Add-AzDoDiscussionComment`, `Set-AzDoPrWorkItemLink`, `Search-AzDoWorkItems`.

## Why This Exists (instead of `az boards` / `az repos`)

The `CLAUDE.md` workflow originally specified `az boards work-item update`, `az repos pr update --work-items`, and `az devops wiki page update` as the canonical commands. Those don't work on the maintainer's Windows machine, and we spent a session establishing the failure is not fixable from PowerShell:

- **Symptom.** `az extension add --name azure-devops` fails with `Pip failed with status code 1`. Debug output traces to `ImportError: DLL load failed while importing pyexpat: The specified module could not be found`.
- **It isn't just pyexpat.** `_ssl` and `_hashlib` fail the same way; only `_socket` imports cleanly. So pip can't run, no extension can install, and any `az` subcommand requiring HTTPS to AzDO would have broken anyway.
- **Reinstalls don't fix it.** We tried the 32-bit MSI (`aka.ms/installazurecliwindows`), then the 64-bit MSI (`aka.ms/installazurecliwindowsx64`), with the elevated full-cleanup sequence (uninstall → wipe `C:\Program Files\Microsoft SDKs\Azure\CLI2` → fresh install with verbose MSI logging). MSI exit code was 0, but the dependent DLLs in the install folder still wouldn't load.
- **Not AV, not MOTW.** Defender cmdlets returned no detections; no Zone.Identifier streams on the .pyd files; no third-party AV registered.
- **Likely cause** is a machine-level DLL-loading policy (Intune/AppLocker, Smart App Control, or a Universal C Runtime servicing gap) that we can't influence from a user-space session.

Rather than continue debugging a host-policy issue, we sidestepped the entire bundled-Python stack. The AzDO REST API doesn't care which transport you use — `Invoke-RestMethod` + a PAT works fine and gives us identical capability for the workflow CLAUDE.md actually needs.

## How To Use It

### Prereqs

Set `AZDO_PAT` as a User environment variable (System → Environment Variables → New under *User variables*). PAT scopes: **Work Items** R/W, **Code** R/W, **Wiki** R/W. Create the PAT at `https://dev.azure.com/ReGenVillages/_usersSettings/tokens`.

The module reads the PAT from `$env:AZDO_PAT` first, then falls back to the User-scope registry value — so newly-set PATs work in fresh shells without needing to restart Claude Code.

Default org is `ReGenVillages`, default project is `VillageOS-API` (hyphen, not space — the display name in some places shows it with a space, but the API path uses the hyphenated form). Override with `-Org` / `-Project` params or `$env:AZDO_ORG` / `$env:AZDO_PROJECT`.

### Common operations

```powershell
Import-Module .\Tools\AzDO.psm1

# Read
Get-AzDoCurrentUser
Get-AzDoWorkItem -Id 5379 -Fields System.Id,System.Title,System.State

# Create a Task under a parent Feature
New-AzDoWorkItem -Type Task -Title 'Add wiki helpers to AzDO toolkit' `
  -Description 'Add Get-AzDoWikiPage and Set-AzDoWikiPage.' `
  -ParentId 5379 -Tags 'azdo','tooling'

# Update fields and state
Update-AzDoWorkItem -Id 5380 -State Active -AssignedTo 'stas@regenvillages.com' `
  -Fields @{ 'Microsoft.VSTS.Common.Priority' = 2 }

# Post a discussion comment (the "show thinking" convention from CLAUDE.md)
Add-AzDoDiscussionComment -Id 5380 -Text 'analysis: REST API works fine for our purposes.'

# Link a PR to a work item
Set-AzDoPrWorkItemLink -WorkItemId 5380 -PullRequestId 361 `
  -RepositoryId <repo-guid> -ProjectId <project-guid>

# WIQL search
Search-AzDoWorkItems -Wiql "SELECT [System.Id], [System.Title] FROM WorkItems
  WHERE [System.State] = 'Active' AND [System.AssignedTo] = @Me"
```

## What It Doesn't Cover (yet)

- **Wiki page reads/writes.** No `Get-AzDoWikiPage` / `Set-AzDoWikiPage` yet — the wiki section of `CLAUDE.md` has a working raw `Invoke-RestMethod` pattern to use directly. Promote into the module if the inline copy starts getting reused.
- **PR creation / branch policy / pipeline runs.** Out of scope for the original "let Claude create work items" ask. Add when needed.
- **Identity rescoping or multi-org switching.** The defaults are hardcoded to this repo's org/project; param overrides exist but there's no profile mechanism.

## How To Extend

`Invoke-AzDo` is the internal helper; it wires up Basic auth from `Get-AzDoPat`, sets the right content type for JSON-Patch (`application/json-patch+json`) when supplied, and unwraps AzDO's error responses into useful exception messages. Any new function should go through it rather than calling `Invoke-RestMethod` directly, so error formatting and auth handling stay consistent.

JSON-Patch ops use `[ordered]` arrays of `@{ op='add'; path='/fields/...'; value=... }` hashtables. For relations (parent links, PR links, attachments), the path is `/relations/-` and the value is `@{ rel=...; url=...; attributes=... }`.

## If The `az` CLI Ever Starts Working

This module isn't a permanent replacement — if the host-policy issue gets fixed (Windows update, IT-side policy change, new machine), the `az` commands in version control history still work. But the module is the canonical transport now: it's tested, has no install dependency, and works in CI on a clean Windows runner.
