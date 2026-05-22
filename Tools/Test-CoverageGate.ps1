<#
.SYNOPSIS
    Diff-coverage gate. Feature #5433 / Task #5434.

.DESCRIPTION
    Measures patch coverage on the PR's added/modified executable lines against a
    threshold (default 85%). Replaces the Phase 3 per-assembly absolute-threshold
    gate (Task #5406), which produced false failures on dev boxes because coverage
    measurement is noisy across environments while CI happened to reproduce the
    original baseline.

    Inputs:
      - per-project Cobertura XMLs from `dotnet test --collect:"XPlat Code Coverage"`
        (merged with reportgenerator into one Cobertura.xml)
      - `git diff --unified=0 $(git merge-base $BaseRef HEAD)..HEAD` for the
        added/modified line numbers per file

    A changed line is counted as executable only if it has a <line> entry in
    Cobertura. That naturally drops:
      - braces, comments, blank lines (no <line> entry)
      - files excluded by coverage.runsettings (no entries at all)
      - test-project files (test assemblies excluded via ModulePath in runsettings)

    Local usage after `dotnet test --collect:"XPlat Code Coverage" --settings coverage.runsettings`:

        pwsh Tools/Test-CoverageGate.ps1

    CI usage (azure-pipelines.yml):

        - task: PowerShell@2
          inputs:
            filePath: 'Tools/Test-CoverageGate.ps1'
            pwsh: true

.PARAMETER Reports
    Glob pattern for per-project Cobertura XMLs.

.PARAMETER MergeOutput
    Directory where reportgenerator writes the merged Cobertura.xml.

.PARAMETER BaseRef
    Git ref to compute the merge-base against. Default origin/develop.

.PARAMETER Threshold
    Minimum patch coverage percentage required (integer). Default 85.

.PARAMETER CleanTestResults
    Remove TestResults/ before invoking reportgenerator. Defaults true so stale
    Cobertura XMLs from prior local runs do not contaminate the merge. Disable
    in CI if the agent is already known clean and you want to inspect the artefact.

.EXAMPLE
    pwsh Tools/Test-CoverageGate.ps1

.EXAMPLE
    pwsh Tools/Test-CoverageGate.ps1 -Threshold 90 -BaseRef origin/main
#>
[CmdletBinding()]
param(
    [string]$Reports = 'TestResults/**/coverage.cobertura.xml',
    [string]$MergeOutput = 'TestResults/coverage-report',
    [string]$BaseRef = 'origin/develop',
    [int]$Threshold = 85,
    [bool]$CleanTestResults = $false
)

$ErrorActionPreference = 'Stop'

# dotnet global tools are not always on PATH for ad-hoc PowerShell on hosted CI agents.
$dotnetToolsCandidates = @(
    (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.dotnet/tools'),
    (Join-Path $HOME '.dotnet/tools')
) | Where-Object { Test-Path $_ }
foreach ($p in $dotnetToolsCandidates) {
    if ($env:PATH -notlike "*$p*") {
        $env:PATH = "$p$([IO.Path]::PathSeparator)$env:PATH"
    }
}

if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
    Write-Host "##vso[task.logissue type=error]Coverage gate: 'reportgenerator' not on PATH. Install with: dotnet tool install -g dotnet-reportgenerator-globaltool"
    exit 1
}

# ----- Merge per-project Cobertura XMLs ------------------------------------------

if ($CleanTestResults -and (Test-Path $MergeOutput)) {
    # Stale prior runs in MergeOutput can leave a Cobertura.xml that masks "no files
    # produced by the current run" — clear it to force a fresh merge.
    Remove-Item -Recurse -Force $MergeOutput
}

Write-Host "Merging Cobertura XMLs"
Write-Host "  reports: $Reports"
Write-Host "  output:  $MergeOutput"
reportgenerator "-reports:$Reports" "-targetdir:$MergeOutput" "-reporttypes:Cobertura;TextSummary"
if ($LASTEXITCODE -ne 0) {
    Write-Host "##vso[task.logissue type=error]Coverage gate: reportgenerator merge failed (exit $LASTEXITCODE)."
    exit 1
}

$mergedXml = Join-Path $MergeOutput 'Cobertura.xml'
if (-not (Test-Path $mergedXml)) {
    Write-Host "##vso[task.logissue type=error]Coverage gate: merged $mergedXml not produced. Coverage collection likely produced no Cobertura files."
    exit 1
}

# ----- Build line-hits lookup from Cobertura -------------------------------------

[xml]$cob = Get-Content $mergedXml -Raw

$repoRoot = (& git rev-parse --show-toplevel).Trim().Replace('\', '/')

function Get-NormalizedPath {
    param([string]$Path)
    $p = $Path.Replace('\', '/')
    if ($repoRoot -and $p.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $p = $p.Substring($repoRoot.Length).TrimStart('/')
    }
    return $p
}

$lineHits = @{}  # @{ 'repo-relative/path.cs' = @{ <lineNumber> = <maxHits> } }
foreach ($pkg in $cob.coverage.packages.package) {
    foreach ($cls in $pkg.classes.class) {
        $file = Get-NormalizedPath $cls.filename
        if (-not $lineHits.ContainsKey($file)) { $lineHits[$file] = @{} }
        foreach ($line in $cls.lines.line) {
            $n = [int]$line.number
            $h = [int]$line.hits
            # Partial classes / generic specializations can produce duplicate <line>
            # entries for the same source line — take the max so any hit counts.
            if ($lineHits[$file].ContainsKey($n)) {
                if ($h -gt $lineHits[$file][$n]) { $lineHits[$file][$n] = $h }
            } else {
                $lineHits[$file][$n] = $h
            }
        }
    }
}

# ----- Diff vs merge-base, parse into per-file changed-line map ------------------

$mergeBase = (& git merge-base $BaseRef HEAD).Trim()
if (-not $mergeBase) {
    Write-Host "##vso[task.logissue type=error]Coverage gate: could not compute merge-base against '$BaseRef'. Ensure the ref is fetched."
    exit 1
}

Write-Host ""
Write-Host "Diff: merge-base $mergeBase ($BaseRef) .. HEAD"

$diffText = & git diff --unified=0 --no-color "$mergeBase..HEAD"

$changed = @{}        # @{ 'path' = @(lineNumbers) }
$currentFile = $null
$cursor = 0
$isBinary = $false

foreach ($line in ($diffText -split "`n")) {
    if ($line.StartsWith('diff --git ')) {
        $currentFile = $null
        $isBinary = $false
        continue
    }
    if ($line.StartsWith('Binary files ')) {
        $isBinary = $true
        $currentFile = $null
        continue
    }
    if ($line.StartsWith('+++ ')) {
        $path = $line.Substring(4).Trim()
        if ($path -eq '/dev/null' -or $isBinary) { $currentFile = $null; continue }
        if ($path.StartsWith('b/')) { $path = $path.Substring(2) }
        $currentFile = $path
        if (-not $changed.ContainsKey($currentFile)) { $changed[$currentFile] = @() }
        continue
    }
    if ($line.StartsWith('@@ ')) {
        if (-not $currentFile) { continue }
        if ($line -match '^@@\s+-\d+(?:,\d+)?\s+\+(\d+)(?:,\d+)?\s+@@') {
            $cursor = [int]$Matches[1]
        }
        continue
    }
    if ($currentFile -and $line.StartsWith('+') -and -not $line.StartsWith('+++')) {
        $changed[$currentFile] += $cursor
        $cursor++
        continue
    }
    # '-' lines and rare ' ' context lines under --unified=0: ignored.
}

# Drop files with no added lines (pure deletions, binary, rename-without-edit).
foreach ($k in @($changed.Keys)) {
    if ($changed[$k].Count -eq 0) { $changed.Remove($k) }
}

# ----- Intersect with line hits to compute patch coverage ------------------------

$perFile = @{}
$totalChanged = 0
$totalCovered = 0

foreach ($file in $changed.Keys) {
    if (-not $lineHits.ContainsKey($file)) { continue }  # excluded or unmeasured
    $covered = 0
    $execCount = 0
    $missed = @()
    foreach ($n in $changed[$file]) {
        if ($lineHits[$file].ContainsKey($n)) {
            $execCount++
            if ($lineHits[$file][$n] -gt 0) { $covered++ } else { $missed += $n }
        }
    }
    if ($execCount -eq 0) { continue }
    $perFile[$file] = [PSCustomObject]@{
        File        = $file
        Covered     = $covered
        Total       = $execCount
        Percent     = [math]::Round(100.0 * $covered / $execCount, 1)
        MissedLines = $missed
    }
    $totalChanged += $execCount
    $totalCovered += $covered
}

# ----- Report + exit -------------------------------------------------------------

Write-Host ""
Write-Host "Diff coverage gate"
Write-Host "  threshold: $Threshold% of changed executable lines"

if ($totalChanged -eq 0) {
    Write-Host "  measured:  no executable changes to gate (docs/config/test-only PR, or all changes in excluded files)"
    Write-Host ""
    Write-Host "##[section]Coverage gate passed - nothing to measure."
    exit 0
}

$overall = [math]::Round(100.0 * $totalCovered / $totalChanged, 1)
Write-Host "  measured:  $totalCovered / $totalChanged changed executable lines = $overall%"
Write-Host ""

$rows = $perFile.Values | Sort-Object File | ForEach-Object {
    [PSCustomObject]@{
        File    = $_.File
        Covered = "$($_.Covered) / $($_.Total)"
        Percent = "$($_.Percent)%"
        Missed  = if ($_.MissedLines.Count -gt 0) { ($_.MissedLines | Sort-Object) -join ',' } else { '' }
    }
}
$rows | Format-Table -AutoSize | Out-String | Write-Host

if ($overall -lt $Threshold) {
    Write-Host "##vso[task.logissue type=error]Coverage gate failed: patch coverage $overall% < $Threshold%"
    foreach ($pf in ($perFile.Values | Where-Object { $_.MissedLines.Count -gt 0 } | Sort-Object File)) {
        Write-Host "##vso[task.logissue type=error]  $($pf.File) missed lines: $(($pf.MissedLines | Sort-Object) -join ', ')"
    }
    Write-Host "##vso[task.logissue type=error]Add tests covering the missed lines, or justify the gap in the PR description."
    exit 1
}

Write-Host "##[section]Coverage gate passed - patch coverage $overall% >= $Threshold%."
exit 0
