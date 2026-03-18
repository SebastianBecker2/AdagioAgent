param(
    [string]$DiagnosticsRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-dryrun-diagnostics'),
    [int]$RetentionDays = 14
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($RetentionDays -lt 0) {
    throw "RetentionDays must be >= 0. Provided: $RetentionDays"
}

if (-not (Test-Path -LiteralPath $DiagnosticsRoot -PathType Container)) {
    Write-Host "Dry-run diagnostics root not found; nothing to prune: $DiagnosticsRoot"
    exit 0
}

$cutoffUtc = [DateTime]::UtcNow.AddDays(-$RetentionDays)
$diagnosticFiles = @(Get-ChildItem -LiteralPath $DiagnosticsRoot -File -Filter '*.json')

$removedCount = 0
$keptCount = 0

foreach ($file in $diagnosticFiles) {
    if ($file.LastWriteTimeUtc -lt $cutoffUtc) {
        Remove-Item -LiteralPath $file.FullName -Force
        $removedCount += 1
    }
    else {
        $keptCount += 1
    }
}

Write-Host "Dry-run diagnostics prune completed. Removed=$removedCount Kept=$keptCount RetentionDays=$RetentionDays Root=$DiagnosticsRoot"
