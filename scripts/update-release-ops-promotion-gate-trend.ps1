param(
    [string]$ReadinessRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [int]$MaxEntries = 20,
    [int]$RetentionDays = 365,
    [switch]$ArchiveLatest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($MaxEntries -le 0) {
    throw "MaxEntries must be > 0. Provided: $MaxEntries"
}

if ($RetentionDays -le 0) {
    throw "RetentionDays must be > 0. Provided: $RetentionDays"
}

if (-not (Test-Path -LiteralPath $ReadinessRoot -PathType Container)) {
    Write-Host "Readiness root not found; promotion gate trend update skipped: $ReadinessRoot"
    exit 0
}

$latestJsonPath = Join-Path $ReadinessRoot 'release-ops-promotion-gate-report.json'
$latestMdPath = Join-Path $ReadinessRoot 'release-ops-promotion-gate-report.md'

if ($ArchiveLatest -and (Test-Path -LiteralPath $latestJsonPath -PathType Leaf)) {
    $latestReport = $null
    try {
        $latestReport = Get-Content -LiteralPath $latestJsonPath -Raw | ConvertFrom-Json
    }
    catch {
        $latestReport = $null
    }

    $generatedAt = [DateTimeOffset]::UtcNow
    if ($latestReport -and $latestReport.PSObject.Properties.Match('generatedAtUtc').Count -gt 0) {
        try {
            $generatedAt = [DateTimeOffset]::Parse([string]$latestReport.generatedAtUtc)
        }
        catch {
            $generatedAt = [DateTimeOffset]::UtcNow
        }
    }

    $stamp = $generatedAt.UtcDateTime.ToString('yyyyMMddHHmmss')
    $archivedJsonPath = Join-Path $ReadinessRoot "release-ops-promotion-gate-report-$stamp.json"
    $archivedMdPath = Join-Path $ReadinessRoot "release-ops-promotion-gate-report-$stamp.md"

    if (-not (Test-Path -LiteralPath $archivedJsonPath -PathType Leaf)) {
        Copy-Item -LiteralPath $latestJsonPath -Destination $archivedJsonPath -Force
    }

    if (Test-Path -LiteralPath $latestMdPath -PathType Leaf) {
        if (-not (Test-Path -LiteralPath $archivedMdPath -PathType Leaf)) {
            Copy-Item -LiteralPath $latestMdPath -Destination $archivedMdPath -Force
        }
    }
}

$cutoff = [DateTime]::UtcNow.AddDays(-$RetentionDays)
$archivedJsonFiles = @(Get-ChildItem -LiteralPath $ReadinessRoot -File -Filter 'release-ops-promotion-gate-report-*.json' |
    Where-Object { $_.Name -ne 'release-ops-promotion-gate-report.json' })

$removedCount = 0
foreach ($file in $archivedJsonFiles) {
    if ($file.LastWriteTimeUtc -lt $cutoff) {
        Remove-Item -LiteralPath $file.FullName -Force
        $removedCount++

        $mdCandidate = [System.IO.Path]::ChangeExtension($file.FullName, '.md')
        if (Test-Path -LiteralPath $mdCandidate -PathType Leaf) {
            Remove-Item -LiteralPath $mdCandidate -Force
        }
    }
}

$reportFiles = @(Get-ChildItem -LiteralPath $ReadinessRoot -File -Filter 'release-ops-promotion-gate-report-*.json' |
    Where-Object {
        $_.Name -ne 'release-ops-promotion-gate-report.json' -and
        $_.Name -ne 'release-ops-promotion-gate-trend-index.json'
    })

$entries = New-Object System.Collections.Generic.List[object]

foreach ($file in $reportFiles) {
    $report = $null
    try {
        $report = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    }
    catch {
        continue
    }

    $generatedAt = [DateTimeOffset]$file.LastWriteTimeUtc
    if ($report.PSObject.Properties.Match('generatedAtUtc').Count -gt 0) {
        try {
            $generatedAt = [DateTimeOffset]::Parse([string]$report.generatedAtUtc)
        }
        catch {
            $generatedAt = [DateTimeOffset]$file.LastWriteTimeUtc
        }
    }

    $verdict = if ($report.PSObject.Properties.Match('promotionVerdict').Count -gt 0) {
        [string]$report.promotionVerdict
    }
    else {
        'unknown'
    }

    $gatePassed = $false
    if ($report.PSObject.Properties.Match('gatePassed').Count -gt 0) {
        $gatePassed = [bool]$report.gatePassed
    }

    $overrideUsed = $false
    if ($report.PSObject.Properties.Match('directorOverride').Count -gt 0 -and $report.directorOverride) {
        if ($report.directorOverride.PSObject.Properties.Match('used').Count -gt 0) {
            $overrideUsed = [bool]$report.directorOverride.used
        }
    }

    $entries.Add([pscustomobject]@{
        fileName = $file.Name
        generatedAtUtc = $generatedAt.ToString('u')
        promotionVerdict = $verdict
        gatePassed = $gatePassed
        directorOverrideUsed = $overrideUsed
    }) | Out-Null
}

$ordered = @($entries | Sort-Object { [DateTime]::Parse($_.generatedAtUtc) } -Descending | Select-Object -First $MaxEntries)
$passCount = @($ordered | Where-Object { $_.promotionVerdict -eq 'pass' }).Count
$directorCount = @($ordered | Where-Object { $_.promotionVerdict -eq 'director-approval-required' }).Count
$failCount = @($ordered | Where-Object { $_.promotionVerdict -eq 'fail' }).Count
$unknownCount = @($ordered | Where-Object { @('pass', 'director-approval-required', 'fail') -notcontains $_.promotionVerdict }).Count
$overrideUsedCount = @($ordered | Where-Object { $_.directorOverrideUsed }).Count
$blockedCount = @($ordered | Where-Object { -not $_.gatePassed }).Count

$trendObject = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    readinessRoot = $ReadinessRoot
    retentionDays = $RetentionDays
    maxEntries = $MaxEntries
    removedStaleEntries = $removedCount
    totalEntries = $ordered.Count
    verdictCounts = [pscustomobject]@{
        pass = $passCount
        directorApprovalRequired = $directorCount
        fail = $failCount
        unknown = $unknownCount
    }
    directorOverrideUsedCount = $overrideUsedCount
    blockedCount = $blockedCount
    entries = $ordered
}

$indexJsonPath = Join-Path $ReadinessRoot 'release-ops-promotion-gate-trend-index.json'
$indexMdPath = Join-Path $ReadinessRoot 'release-ops-promotion-gate-trend-index.md'

$trendObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $indexJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops Promotion Gate Trend Index') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($trendObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- TotalEntries: $($trendObject.totalEntries)") | Out-Null
$mdLines.Add("- RemovedStaleEntries: $($trendObject.removedStaleEntries)") | Out-Null
$mdLines.Add("- PassCount: $($trendObject.verdictCounts.pass)") | Out-Null
$mdLines.Add("- DirectorApprovalRequiredCount: $($trendObject.verdictCounts.directorApprovalRequired)") | Out-Null
$mdLines.Add("- FailCount: $($trendObject.verdictCounts.fail)") | Out-Null
$mdLines.Add("- OverrideUsedCount: $($trendObject.directorOverrideUsedCount)") | Out-Null
$mdLines.Add("- BlockedCount: $($trendObject.blockedCount)") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Recent Promotion Gate Outcomes') | Out-Null
$mdLines.Add('') | Out-Null

if ($ordered.Count -eq 0) {
    $mdLines.Add('- No archived promotion gate reports found.') | Out-Null
}
else {
    foreach ($entry in $ordered) {
        $mdLines.Add("- [$($entry.promotionVerdict)] $($entry.generatedAtUtc) :: gatePassed=$($entry.gatePassed) :: overrideUsed=$($entry.directorOverrideUsed) :: $($entry.fileName)") | Out-Null
    }
}

Set-Content -LiteralPath $indexMdPath -Value ($mdLines -join "`n") -Encoding UTF8
Write-Host "Release-ops promotion gate trend index updated: $indexJsonPath"
