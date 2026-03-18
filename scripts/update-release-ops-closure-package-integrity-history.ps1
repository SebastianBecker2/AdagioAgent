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
    Write-Host "Readiness root not found; closure integrity history update skipped: $ReadinessRoot"
    exit 0
}

$latestJsonPath = Join-Path $ReadinessRoot 'release-ops-closure-package-integrity-report.json'
$latestMdPath = Join-Path $ReadinessRoot 'release-ops-closure-package-integrity-report.md'

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

    $tagName = 'unknown-tag'
    if ($latestReport -and $latestReport.PSObject.Properties.Match('tagName').Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$latestReport.tagName)) {
        $tagName = [string]$latestReport.tagName
    }

    $safeTag = ($tagName -replace '[^a-zA-Z0-9\.-]', '-')
    $stamp = $generatedAt.UtcDateTime.ToString('yyyyMMddHHmmss')
    $archivedJsonPath = Join-Path $ReadinessRoot ("release-ops-closure-package-integrity-report-$stamp-$safeTag.json")
    $archivedMdPath = Join-Path $ReadinessRoot ("release-ops-closure-package-integrity-report-$stamp-$safeTag.md")

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
$archivedJsonFiles = @(Get-ChildItem -LiteralPath $ReadinessRoot -File -Filter 'release-ops-closure-package-integrity-report-*.json' |
    Where-Object { $_.Name -ne 'release-ops-closure-package-integrity-report.json' })

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

$reportFiles = @(Get-ChildItem -LiteralPath $ReadinessRoot -File -Filter 'release-ops-closure-package-integrity-report-*.json' |
    Where-Object {
        $_.Name -ne 'release-ops-closure-package-integrity-report.json' -and
        $_.Name -ne 'release-ops-closure-package-integrity-history-index.json'
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

    $verdict = if ($report.PSObject.Properties.Match('integrityVerdict').Count -gt 0) {
        [string]$report.integrityVerdict
    }
    else {
        'unknown'
    }

    $issueCount = 0
    if ($report.PSObject.Properties.Match('issueCount').Count -gt 0) {
        $issueCount = [int]$report.issueCount
    }

    $manifestSha = ''
    if ($report.PSObject.Properties.Match('manifest').Count -gt 0 -and $report.manifest) {
        if ($report.manifest.PSObject.Properties.Match('sha256').Count -gt 0) {
            $manifestSha = [string]$report.manifest.sha256
        }
    }

    $entries.Add([pscustomobject]@{
        fileName = $file.Name
        generatedAtUtc = $generatedAt.ToString('u')
        tagName = [string]$report.tagName
        integrityVerdict = $verdict
        issueCount = $issueCount
        manifestSha256 = $manifestSha
    }) | Out-Null
}

$ordered = @($entries | Sort-Object { [DateTime]::Parse($_.generatedAtUtc) } -Descending | Select-Object -First $MaxEntries)

$passCount = @($ordered | Where-Object { $_.integrityVerdict -eq 'pass' }).Count
$failCount = @($ordered | Where-Object { $_.integrityVerdict -eq 'fail' }).Count
$unknownCount = @($ordered | Where-Object { @('pass', 'fail') -notcontains $_.integrityVerdict }).Count
$manifestHashes = @($ordered | ForEach-Object { [string]$_.manifestSha256 } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)

$historyObject = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    readinessRoot = $ReadinessRoot
    retentionDays = $RetentionDays
    maxEntries = $MaxEntries
    removedStaleEntries = $removedCount
    totalEntries = $ordered.Count
    verdictCounts = [pscustomobject]@{
        pass = $passCount
        fail = $failCount
        unknown = $unknownCount
    }
    uniqueManifestHashCount = $manifestHashes.Count
    entries = $ordered
}

$indexJsonPath = Join-Path $ReadinessRoot 'release-ops-closure-package-integrity-history-index.json'
$indexMdPath = Join-Path $ReadinessRoot 'release-ops-closure-package-integrity-history-index.md'

$historyObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $indexJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops Closure Package Integrity History Index') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($historyObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- TotalEntries: $($historyObject.totalEntries)") | Out-Null
$mdLines.Add("- RemovedStaleEntries: $($historyObject.removedStaleEntries)") | Out-Null
$mdLines.Add("- PassCount: $($historyObject.verdictCounts.pass)") | Out-Null
$mdLines.Add("- FailCount: $($historyObject.verdictCounts.fail)") | Out-Null
$mdLines.Add("- UniqueManifestHashCount: $($historyObject.uniqueManifestHashCount)") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Recent Closure Integrity Reports') | Out-Null
$mdLines.Add('') | Out-Null

if ($ordered.Count -eq 0) {
    $mdLines.Add('- No archived closure integrity reports found.') | Out-Null
}
else {
    foreach ($entry in $ordered) {
        $mdLines.Add("- [$($entry.integrityVerdict)] $($entry.generatedAtUtc) :: $($entry.tagName) :: issueCount=$($entry.issueCount) :: manifestSha=$($entry.manifestSha256) :: $($entry.fileName)") | Out-Null
    }
}

Set-Content -LiteralPath $indexMdPath -Value ($mdLines -join "`n") -Encoding UTF8
Write-Host "Release-ops closure package integrity history index updated: $indexJsonPath"
