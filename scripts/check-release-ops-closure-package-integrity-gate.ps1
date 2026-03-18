param(
    [string]$ReadinessRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [int]$MinRecentPassCount = 1,
    [int]$RecentWindowCount = 5,
    [switch]$FailOnBlock
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($MinRecentPassCount -le 0) {
    throw "MinRecentPassCount must be > 0. Provided: $MinRecentPassCount"
}

if ($RecentWindowCount -le 0) {
    throw "RecentWindowCount must be > 0. Provided: $RecentWindowCount"
}

if (-not (Test-Path -LiteralPath $ReadinessRoot -PathType Container)) {
    throw "Readiness root not found: $ReadinessRoot"
}

$indexPath = Join-Path $ReadinessRoot 'release-ops-closure-package-integrity-history-index.json'

if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Closure package integrity history index not found: $indexPath"
}

$historyIndex = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json

$entries = @()
if ($historyIndex.PSObject.Properties.Match('entries').Count -gt 0 -and $historyIndex.entries) {
    $entries = @($historyIndex.entries)
}

$recentEntries = @($entries | Select-Object -First $RecentWindowCount)
$recentPassCount = @($recentEntries | Where-Object { [string]$_.integrityVerdict -eq 'pass' }).Count

$gateVerdict = if ($recentPassCount -ge $MinRecentPassCount) { 'pass' } else { 'block' }

$blockedReasons = New-Object System.Collections.Generic.List[string]
if ($gateVerdict -eq 'block') {
    $blockedReasons.Add("Insufficient recent pass verdicts: required=$MinRecentPassCount, found=$recentPassCount in last $RecentWindowCount entries.") | Out-Null
}

$gateObject = [pscustomobject]@{
    generatedAtUtc       = [DateTimeOffset]::UtcNow.ToString('o')
    gateVerdict          = $gateVerdict
    minRecentPassCount   = $MinRecentPassCount
    recentWindowCount    = $RecentWindowCount
    recentEntryCount     = $recentEntries.Count
    recentPassCount      = $recentPassCount
    blockedReasonCount   = $blockedReasons.Count
    blockedReasons       = $blockedReasons.ToArray()
    indexPath            = $indexPath
}

$outputJsonPath = Join-Path $ReadinessRoot 'release-ops-closure-package-integrity-gate-report.json'
$outputMdPath   = Join-Path $ReadinessRoot 'release-ops-closure-package-integrity-gate-report.md'

$gateObject | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $outputJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops Closure Package Integrity Gate Report') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($gateObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- GateVerdict: **$($gateVerdict.ToUpper())**") | Out-Null
$mdLines.Add("- MinRecentPassCount: $MinRecentPassCount") | Out-Null
$mdLines.Add("- RecentWindowCount: $RecentWindowCount") | Out-Null
$mdLines.Add("- RecentEntryCount: $($recentEntries.Count)") | Out-Null
$mdLines.Add("- RecentPassCount: $recentPassCount") | Out-Null

if ($blockedReasons.Count -gt 0) {
    $mdLines.Add('') | Out-Null
    $mdLines.Add('## Blocked Reasons') | Out-Null
    $mdLines.Add('') | Out-Null
    foreach ($reason in $blockedReasons) {
        $mdLines.Add("- $reason") | Out-Null
    }
}

Set-Content -LiteralPath $outputMdPath -Value ($mdLines -join "`n") -Encoding UTF8

Write-Host "Release-ops closure package integrity gate report written: $outputJsonPath (gateVerdict=$gateVerdict)"

if ($FailOnBlock -and $gateVerdict -eq 'block') {
    throw "Closure package integrity gate BLOCKED: $($blockedReasons[0])"
}
