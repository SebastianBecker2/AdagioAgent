param(
    [string]$DiagnosticsRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-dryrun-diagnostics'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [string]$TagName,
    [string]$CiStatusReportPath,
    [switch]$FailOnHold
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not $TagName) {
    $TagName = if ($env:APPVEYOR_REPO_TAG_NAME) {
        $env:APPVEYOR_REPO_TAG_NAME
    }
    elseif ($env:GITHUB_REF_NAME) {
        $env:GITHUB_REF_NAME
    }
    else {
        ''
    }
}

$isTaggedBuild = ($env:APPVEYOR_REPO_TAG -eq 'true') -or ($env:GITHUB_REF_TYPE -eq 'tag') -or (-not [string]::IsNullOrWhiteSpace($TagName))

if (-not $isTaggedBuild -or [string]::IsNullOrWhiteSpace($TagName)) {
    Write-Host 'Release-ops tag readiness summary skipped (not a tagged build).'
    return
}

if ($TagName -notmatch '^v(\d+\.\d+\.\d+)$') {
    throw "Tag '$TagName' is not SemVer formatted as v<major.minor.patch>."
}

if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$validatorScripts = @(
    [pscustomobject]@{ name = 'signoffEvidenceReferences'; path = (Join-Path $PSScriptRoot 'check-signoff-evidence-references.ps1') },
    [pscustomobject]@{ name = 'signoffEvidenceIndexReference'; path = (Join-Path $PSScriptRoot 'check-signoff-evidence-index-reference.ps1') },
    [pscustomobject]@{ name = 'evidenceIndexContent'; path = (Join-Path $PSScriptRoot 'check-evidence-index-content.ps1') }
)

$validatorResults = New-Object System.Collections.Generic.List[object]

foreach ($validator in $validatorScripts) {
    if (-not (Test-Path -LiteralPath $validator.path -PathType Leaf)) {
        $validatorResults.Add([pscustomobject]@{
            name = $validator.name
            script = $validator.path
            passed = $false
            message = "Validator script missing: $($validator.path)"
        }) | Out-Null
        continue
    }

    try {
        & $validator.path
        $validatorResults.Add([pscustomobject]@{
            name = $validator.name
            script = $validator.path
            passed = $true
            message = 'Passed'
        }) | Out-Null
    }
    catch {
        $validatorResults.Add([pscustomobject]@{
            name = $validator.name
            script = $validator.path
            passed = $false
            message = $_.Exception.Message
        }) | Out-Null
    }
}

$ciStatusSummary = [pscustomobject]@{
    available = $false
    overallStatus = 'no-data'
    trendLevel = 'no-data'
    indexFreshPassed = $true
    trendGatePassed = $true
    message = 'CI status report not available.'
}

if (-not $CiStatusReportPath) {
    $generatedReportPath = Join-Path $OutputDir 'release-ops-ci-status-report.json'
    try {
        & (Join-Path $PSScriptRoot 'generate-release-ops-ci-status-report.ps1') -DiagnosticsRoot $DiagnosticsRoot -OutputDir $OutputDir | Out-Null
        if (Test-Path -LiteralPath $generatedReportPath -PathType Leaf) {
            $CiStatusReportPath = $generatedReportPath
        }
    }
    catch {
        # Keep no-data state and include failure note.
        $ciStatusSummary.message = "Unable to generate CI status report: $($_.Exception.Message)"
    }
}

if ($CiStatusReportPath -and (Test-Path -LiteralPath $CiStatusReportPath -PathType Leaf)) {
    try {
        $statusObj = Get-Content -LiteralPath $CiStatusReportPath -Raw | ConvertFrom-Json
        $ciStatusSummary = [pscustomobject]@{
            available = $true
            overallStatus = [string]$statusObj.overallStatus
            trendLevel = [string]$statusObj.qualityGates.trendGate.level
            indexFreshPassed = [bool]$statusObj.qualityGates.indexFresh.passed
            trendGatePassed = [bool]$statusObj.qualityGates.trendGate.passed
            message = [string]$statusObj.qualityGates.trendGate.message
        }
    }
    catch {
        $ciStatusSummary.message = "Unable to parse CI status report '$CiStatusReportPath': $($_.Exception.Message)"
    }
}

$failedValidators = @($validatorResults | Where-Object { -not $_.passed })
$allValidatorsPassed = ($failedValidators.Count -eq 0)
$validatorResultsArray = $validatorResults.ToArray()
$passedValidatorsCount = @($validatorResultsArray | Where-Object { $_.passed }).Count

$readinessVerdict = 'ready'
$readinessMessage = 'Tagged-release readiness checks passed.'

if (-not $allValidatorsPassed) {
    $readinessVerdict = 'hold'
    $readinessMessage = "One or more required tagged-release validators failed ($($failedValidators.Count))."
}
elseif ($ciStatusSummary.overallStatus -eq 'escalate') {
    $readinessVerdict = 'hold'
    $readinessMessage = 'CI diagnostics trend level is escalate. Halt release approval until stabilized.'
}
elseif ($ciStatusSummary.overallStatus -eq 'hold') {
    $readinessVerdict = 'hold'
    $readinessMessage = 'CI diagnostics trend level is hold. Resolve trend issues before release approval.'
}
elseif ($ciStatusSummary.overallStatus -eq 'pass-with-note') {
    $readinessVerdict = 'ready-with-note'
    $readinessMessage = 'Tagged-release checks passed with minor CI trend notes. Record notes in sign-off.'
}
elseif ($ciStatusSummary.overallStatus -eq 'no-data') {
    $readinessVerdict = 'ready-with-note'
    $readinessMessage = 'Tagged-release validators passed; CI trend data unavailable.'
}

$reportObject = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    tagName = $TagName
    readinessVerdict = $readinessVerdict
    readinessMessage = $readinessMessage
    validatorSummary = [pscustomobject]@{
        total = $validatorResultsArray.Count
        passed = $passedValidatorsCount
        failed = $failedValidators.Count
        results = $validatorResultsArray
    }
    diagnosticsQualityGate = $ciStatusSummary
}

$summaryJsonPath = Join-Path $OutputDir 'release-ops-tag-readiness-summary.json'
$summaryMdPath = Join-Path $OutputDir 'release-ops-tag-readiness-summary.md'

$reportObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops Tag Readiness Summary') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($reportObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- TagName: $TagName") | Out-Null
$mdLines.Add("- ReadinessVerdict: **$($readinessVerdict.ToUpper())**") | Out-Null
$mdLines.Add("- ReadinessMessage: $readinessMessage") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Validator Results') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- Total: $($reportObject.validatorSummary.total)") | Out-Null
$mdLines.Add("- Passed: $($reportObject.validatorSummary.passed)") | Out-Null
$mdLines.Add("- Failed: $($reportObject.validatorSummary.failed)") | Out-Null
$mdLines.Add('') | Out-Null

foreach ($result in $validatorResultsArray) {
    $status = if ($result.passed) { 'PASS' } else { 'FAIL' }
    $mdLines.Add("- [$status] $($result.name): $($result.message)") | Out-Null
}

$mdLines.Add('') | Out-Null
$mdLines.Add('## Diagnostics Quality Gate') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- Available: $($ciStatusSummary.available)") | Out-Null
$mdLines.Add("- OverallStatus: $($ciStatusSummary.overallStatus)") | Out-Null
$mdLines.Add("- TrendLevel: $($ciStatusSummary.trendLevel)") | Out-Null
$mdLines.Add("- IndexFreshPassed: $($ciStatusSummary.indexFreshPassed)") | Out-Null
$mdLines.Add("- TrendGatePassed: $($ciStatusSummary.trendGatePassed)") | Out-Null
$mdLines.Add("- Message: $($ciStatusSummary.message)") | Out-Null

Set-Content -LiteralPath $summaryMdPath -Value ($mdLines -join "`n") -Encoding UTF8
Write-Host "Release-ops tag readiness summary written: $summaryJsonPath (readinessVerdict=$readinessVerdict)"

if ($FailOnHold -and ($readinessVerdict -eq 'hold')) {
    throw "Release tag readiness verdict is HOLD. See $summaryJsonPath"
}

