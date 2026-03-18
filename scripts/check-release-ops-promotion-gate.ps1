param(
    [string]$ReadinessRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [string]$HistoryIndexPath,
    [int]$RequiredRecentReadyCount = 3,
    [int]$NoHoldInRecentCount = 2,
    [switch]$AllowDirectorOverride,
    [string]$DirectorApprovalReference,
    [switch]$FailOnBlock
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $HistoryIndexPath) {
    $HistoryIndexPath = Join-Path $ReadinessRoot 'release-ops-tag-readiness-history-index.json'
}

if ($RequiredRecentReadyCount -le 0) {
    throw "RequiredRecentReadyCount must be > 0. Provided: $RequiredRecentReadyCount"
}

if ($NoHoldInRecentCount -le 0) {
    throw "NoHoldInRecentCount must be > 0. Provided: $NoHoldInRecentCount"
}

if ($AllowDirectorOverride -and [string]::IsNullOrWhiteSpace($DirectorApprovalReference)) {
    throw 'DirectorApprovalReference is required when AllowDirectorOverride is set.'
}

if (-not (Test-Path -LiteralPath $HistoryIndexPath -PathType Leaf)) {
    throw "Tag readiness history index not found: $HistoryIndexPath"
}

$index = Get-Content -LiteralPath $HistoryIndexPath -Raw | ConvertFrom-Json
$entries = @()
if ($index.PSObject.Properties.Match('entries').Count -gt 0 -and $index.entries) {
    $entries = @($index.entries)
}

$gatePassed = $false
$promotionVerdict = 'fail'
$decisionReason = ''
$directorOverrideUsed = $false

if ($entries.Count -eq 0) {
    $decisionReason = 'No readiness history entries are available. Generate tagged readiness summaries before promotion.'
}
else {
    $recentNoHold = @($entries | Select-Object -First $NoHoldInRecentCount)
    $holdInRecent = @($recentNoHold | Where-Object { [string]$_.readinessVerdict -eq 'hold' }).Count

    $recentRequired = @($entries | Select-Object -First $RequiredRecentReadyCount)
    $readyCount = @($recentRequired | Where-Object { [string]$_.readinessVerdict -eq 'ready' }).Count
    $readyWithNoteCount = @($recentRequired | Where-Object { [string]$_.readinessVerdict -eq 'ready-with-note' }).Count
    $unexpectedCount = @($recentRequired | Where-Object { @('ready', 'ready-with-note') -notcontains [string]$_.readinessVerdict }).Count

    if ($holdInRecent -gt 0) {
        $promotionVerdict = 'fail'
        $decisionReason = "Promotion blocked: $holdInRecent hold verdict(s) detected in the latest $NoHoldInRecentCount tagged summaries."
    }
    elseif ($recentRequired.Count -lt $RequiredRecentReadyCount) {
        $promotionVerdict = 'fail'
        $decisionReason = "Promotion blocked: only $($recentRequired.Count) readiness entr$(if ($recentRequired.Count -eq 1) { 'y' } else { 'ies' }) available; need $RequiredRecentReadyCount."
    }
    elseif ($readyCount -eq $RequiredRecentReadyCount) {
        $promotionVerdict = 'pass'
        $gatePassed = $true
        $decisionReason = "Promotion gate passed: latest $RequiredRecentReadyCount tagged summaries are all 'ready'."
    }
    elseif ($unexpectedCount -eq 0 -and ($readyCount + $readyWithNoteCount) -eq $RequiredRecentReadyCount -and $readyWithNoteCount -gt 0) {
        $promotionVerdict = 'director-approval-required'
        $decisionReason = "Promotion requires director approval: latest $RequiredRecentReadyCount summaries include $readyWithNoteCount 'ready-with-note' verdict(s)."

        if ($AllowDirectorOverride) {
            $gatePassed = $true
            $directorOverrideUsed = $true
            $decisionReason += " Override accepted with reference '$DirectorApprovalReference'."
        }
    }
    else {
        $promotionVerdict = 'fail'
        $decisionReason = "Promotion blocked: latest $RequiredRecentReadyCount summaries do not meet readiness threshold (ready=$readyCount, ready-with-note=$readyWithNoteCount, unexpected=$unexpectedCount)."
    }
}

$reportObject = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    historyIndexPath = $HistoryIndexPath
    promotionVerdict = $promotionVerdict
    gatePassed = $gatePassed
    decisionReason = $decisionReason
    thresholds = [pscustomobject]@{
        requiredRecentReadyCount = $RequiredRecentReadyCount
        noHoldInRecentCount = $NoHoldInRecentCount
    }
    directorOverride = [pscustomobject]@{
        allowed = [bool]$AllowDirectorOverride
        used = $directorOverrideUsed
        reference = $DirectorApprovalReference
    }
    summary = [pscustomobject]@{
        totalEntries = $entries.Count
        latestEntries = @($entries | Select-Object -First ([Math]::Max($RequiredRecentReadyCount, $NoHoldInRecentCount)))
    }
}

$reportJsonPath = Join-Path $ReadinessRoot 'release-ops-promotion-gate-report.json'
$reportMdPath = Join-Path $ReadinessRoot 'release-ops-promotion-gate-report.md'

$reportObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops Promotion Gate Report') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($reportObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- PromotionVerdict: **$($promotionVerdict.ToUpper())**") | Out-Null
$mdLines.Add("- GatePassed: $gatePassed") | Out-Null
$mdLines.Add("- DecisionReason: $decisionReason") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Thresholds') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- RequiredRecentReadyCount: $RequiredRecentReadyCount") | Out-Null
$mdLines.Add("- NoHoldInRecentCount: $NoHoldInRecentCount") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Director Override') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- Allowed: $($reportObject.directorOverride.allowed)") | Out-Null
$mdLines.Add("- Used: $($reportObject.directorOverride.used)") | Out-Null
$mdLines.Add("- Reference: $($reportObject.directorOverride.reference)") | Out-Null

Set-Content -LiteralPath $reportMdPath -Value ($mdLines -join "`n") -Encoding UTF8
Write-Host "Release-ops promotion gate report written: $reportJsonPath (promotionVerdict=$promotionVerdict, gatePassed=$gatePassed)"

if ($FailOnBlock -and -not $gatePassed) {
    throw "Release-ops promotion gate failed with verdict '$promotionVerdict'. See $reportJsonPath"
}
