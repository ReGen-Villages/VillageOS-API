<#
.SYNOPSIS
    Per-assembly coverage gate. Phase 3 / Task #5406.

.DESCRIPTION
    Merges per-project Cobertura XMLs (produced by `dotnet test --collect:"XPlat Code
    Coverage"`) into one report, then compares each assembly's line + branch coverage
    against an inline threshold hashtable. Exits non-zero on any drop below threshold.

    Runs the same way locally and in CI. Local usage after `dotnet test`:

        pwsh Tools/Test-CoverageGate.ps1

    CI usage (in azure-pipelines.yml):

        - task: PowerShell@2
          inputs:
            filePath: 'Tools/Test-CoverageGate.ps1'
            pwsh: true

    Thresholds are the single source of truth - defined in the $thresholds hashtable
    below. To bump a threshold when coverage improves, update the value in the same PR
    that lands the test work. To add a new assembly, add a row to the hashtable using
    its measured per-package line/branch percent (rounded down). See
    docs/TEST-STATE.md > "Coverage gate (Phase 3 / Task #5406)" for the design rationale.

.PARAMETER Reports
    Glob pattern for per-project Cobertura XMLs. Default matches the test runner's
    output layout under TestResults/.

.PARAMETER MergeOutput
    Directory where ReportGenerator writes the merged Cobertura.xml + HtmlSummary.
    Default is a sibling of the Reports tree.

.EXAMPLE
    pwsh Tools/Test-CoverageGate.ps1

.EXAMPLE
    pwsh Tools/Test-CoverageGate.ps1 -Reports "$(testResultsDirectory)/**/coverage.cobertura.xml" -MergeOutput "$(testResultsDirectory)/coverage-report"
#>
[CmdletBinding()]
param(
    [string]$Reports = 'TestResults/**/coverage.cobertura.xml',
    [string]$MergeOutput = 'TestResults/coverage-report'
)

$ErrorActionPreference = 'Stop'

# Per-assembly thresholds (line / branch, integer percent). Set at each assembly's
# develop-tip value rounded down when the gate was first enabled. To raise: update the
# value here in the same PR that lands the test work. To add: measure the new
# assembly's per-package line/branch rates from a local coverage run, add the row.
$thresholds = @{
    'vos.Application'                             = @{ Line = 98;  Branch = 94 }
    'vos.Auth.Shared'                             = @{ Line = 100; Branch = 100 }
    'vos.CLI'                                     = @{ Line = 94;  Branch = 89 }
    'vos.Core'                                    = @{ Line = 95;  Branch = 89 }
    'vos.Infrastructure'                          = @{ Line = 98;  Branch = 88 }
    'vos.ManagedMicroservice.Delta'               = @{ Line = 94;  Branch = 89 }
    'vos.ManagedMicroservice.Echo'                = @{ Line = 21;  Branch = 45 }
    'vos.ManagedMicroservice.Metabolism'          = @{ Line = 85;  Branch = 81 }
    'vos.ManagedMicroservice.Tributary'           = @{ Line = 95;  Branch = 91 }
    'vos.Microservice.Shared'                     = @{ Line = 100; Branch = 100 }
    'vos.Tests.Shared'                            = @{ Line = 100; Branch = 100 }
}

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

Write-Host "Merging Cobertura XMLs"
Write-Host "  reports: $Reports"
Write-Host "  output:  $MergeOutput"
reportgenerator "-reports:$Reports" "-targetdir:$MergeOutput" "-reporttypes:Cobertura;HtmlSummary;TextSummary"
if ($LASTEXITCODE -ne 0) {
    Write-Host "##vso[task.logissue type=error]Coverage gate: reportgenerator merge failed (exit $LASTEXITCODE)."
    exit 1
}

$mergedXml = Join-Path $MergeOutput 'Cobertura.xml'
if (-not (Test-Path $mergedXml)) {
    Write-Host "##vso[task.logissue type=error]Coverage gate: merged $mergedXml not produced. Coverage collection likely produced no Cobertura files."
    exit 1
}

[xml]$cob = Get-Content $mergedXml
$rows = @()
$failures = @()

foreach ($pkg in $cob.coverage.packages.package) {
    $name   = $pkg.name
    $line   = [math]::Round([double]$pkg.'line-rate'   * 100, 2)
    $branch = [math]::Round([double]$pkg.'branch-rate' * 100, 2)

    if (-not $thresholds.ContainsKey($name)) {
        Write-Host "##vso[task.logissue type=warning]Coverage gate: no threshold defined for assembly '$name'. Add a row to the hashtable in Tools/Test-CoverageGate.ps1."
        $rows += [PSCustomObject]@{ Assembly = $name; Line = "$line%"; Branch = "$branch%"; Status = 'NO-THRESHOLD' }
        continue
    }

    $req = $thresholds[$name]
    $lineOk   = $line   -ge $req.Line
    $branchOk = $branch -ge $req.Branch
    $status = if ($lineOk -and $branchOk) { 'PASS' } else { 'FAIL' }

    $rows += [PSCustomObject]@{
        Assembly = $name
        Line     = "$line% >= $($req.Line)%"
        Branch   = "$branch% >= $($req.Branch)%"
        Status   = $status
    }

    if (-not $lineOk)   { $failures += "$name line coverage $line% below threshold $($req.Line)%" }
    if (-not $branchOk) { $failures += "$name branch coverage $branch% below threshold $($req.Branch)%" }
}

# Surface every assembly's status, sorted so PASS rows group together
$rows | Sort-Object Status, Assembly | Format-Table -AutoSize | Out-String | Write-Host

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "##vso[task.logissue type=error]Coverage gate failed:"
    $failures | ForEach-Object { Write-Host "##vso[task.logissue type=error]  $_" }
    Write-Host ''
    Write-Host "##vso[task.logissue type=error]Fix the regression, or update the threshold in Tools/Test-CoverageGate.ps1 (justify in the PR description)."
    exit 1
}

Write-Host "##[section]Coverage gate passed - all $($rows.Count) assemblies at or above threshold."

