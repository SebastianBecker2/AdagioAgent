param(
    [string]$DiagnosticsRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-dryrun-diagnostics'),
    [int]$MaxAgeSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($MaxAgeSeconds -le 0) {
    throw "MaxAgeSeconds must be > 0. Provided: $MaxAgeSeconds"
}

if (-not (Test-Path -LiteralPath $DiagnosticsRoot -PathType Container)) {
    Write-Host "Diagnostics root not found; freshness check skipped: $DiagnosticsRoot"
    exit 0
}

$summaryFiles = @(Get-ChildItem -LiteralPath $DiagnosticsRoot -File -Filter '*.json' |
    Where-Object {
        $_.Name -like 'dryrun-validation-summary*' -and
        $_.Name -notlike '*latest-success*' -and
        $_.Name -ne 'dryrun-diagnostics-index.json'
    })

if ($summaryFiles.Count -eq 0) {
    Write-Host 'No dry-run summary files found in diagnostics root; freshness check skipped.'
    exit 0
}

$indexPath = Join-Path $DiagnosticsRoot 'dryrun-diagnostics-index.json'

if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Diagnostics index not found but $($summaryFiles.Count) summary file(s) exist. Run update-release-ops-diagnostics-index.ps1 to populate the index."
}

$newest = $summaryFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
$indexFile = Get-Item -LiteralPath $indexPath
$ageOffsetSeconds = ($indexFile.LastWriteTimeUtc - $newest.LastWriteTimeUtc).TotalSeconds

if ($ageOffsetSeconds -lt -$MaxAgeSeconds) {
    throw "Diagnostics index is stale: index was last written $([Math]::Round([Math]::Abs($ageOffsetSeconds)))s before the newest summary '$($newest.Name)'. Re-run update-release-ops-diagnostics-index.ps1 to refresh the index."
}

Write-Host "Diagnostics index freshness check passed (newest summary: '$($newest.Name)', index age offset: $([Math]::Round($ageOffsetSeconds, 1))s, threshold: -${MaxAgeSeconds}s)."
