param(
    [string]$DiagnosticsRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-dryrun-diagnostics'),
    [string]$OutputDir,
    [int]$FreshnessMaxAgeSeconds = 300,
    [int]$RecentEntryCount = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $OutputDir) {
    $OutputDir = $DiagnosticsRoot
}

if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# ── Freshness gate ────────────────────────────────────────────────────────────

$indexPath = Join-Path $DiagnosticsRoot 'dryrun-diagnostics-index.json'
$indexFreshPassed = $true
$indexFreshMessage = 'No diagnostics summaries found; freshness check skipped.'

if (Test-Path -LiteralPath $DiagnosticsRoot -PathType Container) {
    $summaryFiles = @(Get-ChildItem -LiteralPath $DiagnosticsRoot -File -Filter '*.json' |
        Where-Object {
            $_.Name -like 'dryrun-validation-summary*' -and
            $_.Name -notlike '*latest-success*' -and
            $_.Name -ne 'dryrun-diagnostics-index.json'
        })

    if ($summaryFiles.Count -gt 0) {
        if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
            $indexFreshPassed = $false
            $indexFreshMessage = "Diagnostics index missing but $($summaryFiles.Count) summary file(s) exist. Run update-release-ops-diagnostics-index.ps1."
        }
        else {
            $newest = $summaryFiles | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
            $indexFile = Get-Item -LiteralPath $indexPath
            $ageOffsetSeconds = ($indexFile.LastWriteTimeUtc - $newest.LastWriteTimeUtc).TotalSeconds
            if ($ageOffsetSeconds -lt -$FreshnessMaxAgeSeconds) {
                $indexFreshPassed = $false
                $indexFreshMessage = "Diagnostics index is stale: written $([Math]::Round([Math]::Abs($ageOffsetSeconds)))s before the newest summary '$($newest.Name)'."
            }
            else {
                $indexFreshMessage = "Diagnostics index is current (age offset: $([Math]::Round($ageOffsetSeconds, 1))s)."
            }
        }
    }
}

# ── Trend gate ────────────────────────────────────────────────────────────────

$trendLevel = 'no-data'
$trendPassed = $true
$trendMessage = 'No diagnostics entries found; trend gate skipped.'
$totalEntries = 0
$successCount = 0
$failureCount = 0

if (Test-Path -LiteralPath $indexPath -PathType Leaf) {
    $indexObj = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
    $totalEntries = [int]$indexObj.totalEntries
    $successCount = [int]$indexObj.successCount
    $failureCount = [int]$indexObj.failureCount

    if ($totalEntries -gt 0) {
        $recentEntries = @($indexObj.entries | Select-Object -First $RecentEntryCount)
        $recentCount = $recentEntries.Count
        $recentSuccess = @($recentEntries | Where-Object { $_.success }).Count
        $recentFailure = @($recentEntries | Where-Object { -not $_.success }).Count

        # Check for 3+ consecutive failures with the same dominant issue category
        $consecutiveSameCategoryFailures = 0
        $lastCategory = $null
        $shouldEscalate = $false
        foreach ($entry in $recentEntries) {
            if (-not $entry.success) {
                $dominantCategory = 'uncategorized'
                if ($entry.PSObject.Properties.Match('issueCategoryCounts').Count -gt 0 -and $entry.issueCategoryCounts) {
                    $cats = @($entry.issueCategoryCounts.PSObject.Properties | Sort-Object { [int]$_.Value } -Descending)
                    if ($cats.Count -gt 0) { $dominantCategory = $cats[0].Name }
                }
                if ($dominantCategory -eq $lastCategory) {
                    $consecutiveSameCategoryFailures++
                }
                else {
                    $consecutiveSameCategoryFailures = 1
                    $lastCategory = $dominantCategory
                }
                if ($consecutiveSameCategoryFailures -ge 3) {
                    $shouldEscalate = $true
                    break
                }
            }
            else {
                $consecutiveSameCategoryFailures = 0
                $lastCategory = $null
            }
        }

        $recentTotalIssues = 0
        foreach ($entry in $recentEntries) {
            if ($entry.PSObject.Properties.Match('issueCount').Count -gt 0) {
                $recentTotalIssues += [int]$entry.issueCount
            }
        }

        $last3 = @($recentEntries | Select-Object -First 3)
        $failuresInLast3 = @($last3 | Where-Object { -not $_.success }).Count

        if ($shouldEscalate) {
            $trendLevel = 'escalate'
            $trendPassed = $false
            $trendMessage = "3+ consecutive failures with the same issue category in recent entries. Assign release-ops owner and halt handoff."
        }
        elseif ($recentCount -ge 2 -and $recentFailure -ge 2) {
            $trendLevel = 'hold'
            $trendPassed = $false
            $trendMessage = "$recentFailure failure(s) in last $recentCount recent entries. Fix root cause before pilot handoff."
        }
        elseif ($failuresInLast3 -ge 1) {
            $trendLevel = 'hold'
            $trendPassed = $false
            $trendMessage = "Failure present in last 3 entries ($failuresInLast3 failure(s)). Resolve and confirm green trend before pilot handoff."
        }
        elseif ($recentSuccess -eq $recentCount -and $recentTotalIssues -eq 0) {
            $trendLevel = 'pass'
            $trendPassed = $true
            $trendMessage = "All $recentSuccess recent entries are SUCCESS with 0 total issues."
        }
        elseif ($recentSuccess -eq $recentCount -and $recentTotalIssues -le 2) {
            $trendLevel = 'pass-with-note'
            $trendPassed = $true
            $trendMessage = "All $recentSuccess recent entries are SUCCESS with $recentTotalIssues minor issue(s). Record in sign-off."
        }
        else {
            $trendLevel = 'pass-with-note'
            $trendPassed = $true
            $trendMessage = "$recentSuccess success / $recentFailure failure in last $recentCount entries, total issues: $recentTotalIssues."
        }
    }
}

# ── Overall status ────────────────────────────────────────────────────────────

$overallStatus = 'no-data'
if ($trendLevel -eq 'escalate') {
    $overallStatus = 'escalate'
}
elseif ($trendLevel -eq 'hold' -or -not $indexFreshPassed) {
    $overallStatus = 'hold'
}
elseif ($trendLevel -eq 'pass') {
    $overallStatus = 'pass'
}
elseif ($trendLevel -eq 'pass-with-note') {
    $overallStatus = 'pass-with-note'
}

# ── Emit report ───────────────────────────────────────────────────────────────

$reportObject = [pscustomobject]@{
    generatedAtUtc    = [DateTimeOffset]::UtcNow.ToString('u')
    diagnosticsRoot   = $DiagnosticsRoot
    overallStatus     = $overallStatus
    qualityGates      = [pscustomobject]@{
        indexFresh = [pscustomobject]@{
            passed  = $indexFreshPassed
            message = $indexFreshMessage
        }
        trendGate  = [pscustomobject]@{
            passed  = $trendPassed
            level   = $trendLevel
            message = $trendMessage
        }
    }
    summary           = [pscustomobject]@{
        totalEntries     = $totalEntries
        successCount     = $successCount
        failureCount     = $failureCount
        recentEntryCount = $RecentEntryCount
    }
}

$reportJsonPath = Join-Path $OutputDir 'release-ops-ci-status-report.json'
$reportMdPath   = Join-Path $OutputDir 'release-ops-ci-status-report.md'

$reportObject | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops CI Status Report') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($reportObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- OverallStatus: **$($overallStatus.ToUpper())**") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Quality Gates') | Out-Null
$mdLines.Add('') | Out-Null
$freshIcon = if ($indexFreshPassed) { 'PASS' } else { 'FAIL' }
$mdLines.Add("- IndexFresh: [$freshIcon] $indexFreshMessage") | Out-Null
$trendIcon = if ($trendPassed) { 'PASS' } else { 'FAIL' }
$mdLines.Add("- TrendGate: [$trendIcon] ($trendLevel) $trendMessage") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Diagnostics Summary') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- TotalEntries: $totalEntries") | Out-Null
$mdLines.Add("- SuccessCount: $successCount") | Out-Null
$mdLines.Add("- FailureCount: $failureCount") | Out-Null
$mdLines.Add("- RecentEntryCount analyzed: $RecentEntryCount") | Out-Null

Set-Content -LiteralPath $reportMdPath -Value ($mdLines -join "`n") -Encoding UTF8
Write-Host "Release-ops CI status report written: $reportJsonPath (overallStatus=$overallStatus)"
