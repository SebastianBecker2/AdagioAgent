param(
    [string]$DiagnosticsRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-dryrun-diagnostics'),
    [int]$MaxEntries = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($MaxEntries -le 0) {
    throw "MaxEntries must be > 0. Provided: $MaxEntries"
}

if (-not (Test-Path -LiteralPath $DiagnosticsRoot -PathType Container)) {
    Write-Host "Diagnostics root not found; no index update performed: $DiagnosticsRoot"
    exit 0
}

$summaryFiles = @(Get-ChildItem -LiteralPath $DiagnosticsRoot -File -Filter '*.json' |
    Where-Object {
        $_.Name -like 'dryrun-validation-summary*' -and
        $_.Name -notlike '*latest-success*' -and
        $_.Name -ne 'dryrun-diagnostics-index.json'
    })

$parsedEntries = New-Object System.Collections.Generic.List[object]

foreach ($file in $summaryFiles) {
    try {
        $raw = Get-Content -LiteralPath $file.FullName -Raw
        $summary = $raw | ConvertFrom-Json
    }
    catch {
        continue
    }

    $generatedAt = $null
    try {
        $generatedAt = [DateTimeOffset]::Parse([string]$summary.generatedAtUtc)
    }
    catch {
        $generatedAt = [DateTimeOffset]$file.LastWriteTimeUtc
    }

    $issues = @()
    if ($summary.issues) {
        $issues = @($summary.issues)
    }

    $issueCategoryCounts = @{}
    foreach ($issue in $issues) {
        $category = if ($issue.category) { [string]$issue.category } else { 'uncategorized' }
        if ($issueCategoryCounts.ContainsKey($category)) {
            $issueCategoryCounts[$category] += 1
        }
        else {
            $issueCategoryCounts[$category] = 1
        }
    }

    $buildMetadata = $null
    if ($summary.PSObject.Properties.Match('buildMetadata').Count -gt 0) {
        $buildMetadata = $summary.buildMetadata
    }

    $parsedEntries.Add([pscustomobject]@{
        fileName = $file.Name
        generatedAtUtc = $generatedAt.ToString('u')
        success = [bool]$summary.success
        error = [string]$summary.error
        issueCount = $issues.Count
        issueCategoryCounts = [pscustomobject]$issueCategoryCounts
        buildMetadata = $buildMetadata
    }) | Out-Null
}

$ordered = @($parsedEntries | Sort-Object { [DateTime]::Parse($_.generatedAtUtc) } -Descending | Select-Object -First $MaxEntries)
$successCount = @($ordered | Where-Object { $_.success }).Count
$failureCount = @($ordered | Where-Object { -not $_.success }).Count

$indexObject = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    diagnosticsRoot = $DiagnosticsRoot
    maxEntries = $MaxEntries
    totalEntries = $ordered.Count
    successCount = $successCount
    failureCount = $failureCount
    entries = $ordered
}

$indexJsonPath = Join-Path $DiagnosticsRoot 'dryrun-diagnostics-index.json'
$indexMdPath = Join-Path $DiagnosticsRoot 'dryrun-diagnostics-index.md'

$indexObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $indexJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Dry-Run Diagnostics Index') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($indexObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- TotalEntries: $($indexObject.totalEntries)") | Out-Null
$mdLines.Add("- SuccessCount: $($indexObject.successCount)") | Out-Null
$mdLines.Add("- FailureCount: $($indexObject.failureCount)") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Recent Entries') | Out-Null
$mdLines.Add('') | Out-Null

if ($ordered.Count -eq 0) {
    $mdLines.Add('- No diagnostics entries found.') | Out-Null
}
else {
    foreach ($entry in $ordered) {
        $status = if ($entry.success) { 'SUCCESS' } else { 'FAILURE' }
        $mdLines.Add("- [$status] $($entry.generatedAtUtc) :: $($entry.fileName) :: issues=$($entry.issueCount)") | Out-Null
    }
}

Set-Content -LiteralPath $indexMdPath -Value ($mdLines -join "`n") -Encoding UTF8
Write-Host "Dry-run diagnostics index updated: $indexJsonPath"
