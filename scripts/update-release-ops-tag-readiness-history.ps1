param(
    [string]$ReadinessRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [int]$MaxEntries = 20,
    [int]$RetentionDays = 180,
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
    Write-Host "Readiness root not found; history update skipped: $ReadinessRoot"
    return
}

$latestJsonPath = Join-Path $ReadinessRoot 'release-ops-tag-readiness-summary.json'
$latestMdPath = Join-Path $ReadinessRoot 'release-ops-tag-readiness-summary.md'

if ($ArchiveLatest -and (Test-Path -LiteralPath $latestJsonPath -PathType Leaf)) {
    $latestSummary = $null
    try {
        $latestSummary = Get-Content -LiteralPath $latestJsonPath -Raw | ConvertFrom-Json
    }
    catch {
        $latestSummary = $null
    }

    $tagName = 'unknown-tag'
    if ($latestSummary -and $latestSummary.PSObject.Properties.Match('tagName').Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$latestSummary.tagName)) {
        $tagName = [string]$latestSummary.tagName
    }

    $generatedAt = [DateTimeOffset]::UtcNow
    if ($latestSummary -and $latestSummary.PSObject.Properties.Match('generatedAtUtc').Count -gt 0) {
        try {
            $generatedAt = [DateTimeOffset]::Parse([string]$latestSummary.generatedAtUtc)
        }
        catch {
            $generatedAt = [DateTimeOffset]::UtcNow
        }
    }

    $safeTag = ($tagName -replace '[^a-zA-Z0-9\.-]', '-')
    $stamp = $generatedAt.UtcDateTime.ToString('yyyyMMddHHmmss')
    $archivedJsonPath = Join-Path $ReadinessRoot ("release-ops-tag-readiness-summary-$stamp-$safeTag.json")
    $archivedMdPath = Join-Path $ReadinessRoot ("release-ops-tag-readiness-summary-$stamp-$safeTag.md")

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
$archivedJsonFiles = @(Get-ChildItem -LiteralPath $ReadinessRoot -File -Filter 'release-ops-tag-readiness-summary-*.json' |
    Where-Object { $_.Name -ne 'release-ops-tag-readiness-summary.json' })

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

$summaryFiles = @(Get-ChildItem -LiteralPath $ReadinessRoot -File -Filter 'release-ops-tag-readiness-summary-*.json' |
    Where-Object {
        $_.Name -ne 'release-ops-tag-readiness-summary.json' -and
        $_.Name -ne 'release-ops-tag-readiness-history-index.json'
    })

$entries = New-Object System.Collections.Generic.List[object]

foreach ($file in $summaryFiles) {
    $summary = $null
    try {
        $summary = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    }
    catch {
        continue
    }

    $generatedAt = [DateTimeOffset]$file.LastWriteTimeUtc
    if ($summary.PSObject.Properties.Match('generatedAtUtc').Count -gt 0) {
        try {
            $generatedAt = [DateTimeOffset]::Parse([string]$summary.generatedAtUtc)
        }
        catch {
            $generatedAt = [DateTimeOffset]$file.LastWriteTimeUtc
        }
    }

    $validatorFailed = 0
    if ($summary.PSObject.Properties.Match('validatorSummary').Count -gt 0 -and $summary.validatorSummary) {
        if ($summary.validatorSummary.PSObject.Properties.Match('failed').Count -gt 0) {
            $validatorFailed = [int]$summary.validatorSummary.failed
        }
    }

    $diagnosticsStatus = 'no-data'
    if ($summary.PSObject.Properties.Match('diagnosticsQualityGate').Count -gt 0 -and $summary.diagnosticsQualityGate) {
        if ($summary.diagnosticsQualityGate.PSObject.Properties.Match('overallStatus').Count -gt 0) {
            $diagnosticsStatus = [string]$summary.diagnosticsQualityGate.overallStatus
        }
    }

    $entries.Add([pscustomobject]@{
        fileName = $file.Name
        generatedAtUtc = $generatedAt.ToString('u')
        tagName = [string]$summary.tagName
        readinessVerdict = [string]$summary.readinessVerdict
        validatorFailed = $validatorFailed
        diagnosticsOverallStatus = $diagnosticsStatus
    }) | Out-Null
}

$ordered = @($entries | Sort-Object { [DateTime]::Parse($_.generatedAtUtc) } -Descending | Select-Object -First $MaxEntries)

$verdictCounts = @{
    ready = 0
    'ready-with-note' = 0
    hold = 0
    unknown = 0
}

foreach ($entry in $ordered) {
    $key = [string]$entry.readinessVerdict
    if ($verdictCounts.ContainsKey($key)) {
        $verdictCounts[$key] += 1
    }
    else {
        $verdictCounts['unknown'] += 1
    }
}

$indexObject = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    readinessRoot = $ReadinessRoot
    retentionDays = $RetentionDays
    maxEntries = $MaxEntries
    removedStaleEntries = $removedCount
    totalEntries = $ordered.Count
    verdictCounts = [pscustomobject]$verdictCounts
    entries = $ordered
}

$indexJsonPath = Join-Path $ReadinessRoot 'release-ops-tag-readiness-history-index.json'
$indexMdPath = Join-Path $ReadinessRoot 'release-ops-tag-readiness-history-index.md'

$indexObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $indexJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops Tag Readiness History Index') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($indexObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- TotalEntries: $($indexObject.totalEntries)") | Out-Null
$mdLines.Add("- RemovedStaleEntries: $($indexObject.removedStaleEntries)") | Out-Null
$mdLines.Add("- ReadyCount: $($indexObject.verdictCounts.ready)") | Out-Null
$mdLines.Add("- ReadyWithNoteCount: $($indexObject.verdictCounts.'ready-with-note')") | Out-Null
$mdLines.Add("- HoldCount: $($indexObject.verdictCounts.hold)") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Recent Tagged Readiness Summaries') | Out-Null
$mdLines.Add('') | Out-Null

if ($ordered.Count -eq 0) {
    $mdLines.Add('- No archived tagged readiness summaries found.') | Out-Null
}
else {
    foreach ($entry in $ordered) {
        $mdLines.Add("- [$($entry.readinessVerdict)] $($entry.generatedAtUtc) :: $($entry.tagName) :: validatorsFailed=$($entry.validatorFailed) :: diagnostics=$($entry.diagnosticsOverallStatus) :: $($entry.fileName)") | Out-Null
    }
}

Set-Content -LiteralPath $indexMdPath -Value ($mdLines -join "`n") -Encoding UTF8
Write-Host "Release-ops tag readiness history index updated: $indexJsonPath"

