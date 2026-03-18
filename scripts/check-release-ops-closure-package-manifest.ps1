param(
    [string]$ReadinessRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [string]$ManifestPath,
    [string]$TagName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$isTaggedBuild = ($env:APPVEYOR_REPO_TAG -eq 'true') -or ($env:GITHUB_REF_TYPE -eq 'tag')
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

if (-not $isTaggedBuild -or [string]::IsNullOrWhiteSpace($TagName)) {
    Write-Host 'Closure package manifest validation skipped (not a tagged build).'
    exit 0
}

if ($TagName -notmatch '^v(\d+\.\d+\.\d+)$') {
    throw "Tag '$TagName' is not SemVer formatted as v<major.minor.patch>."
}

$expectedVersion = $Matches[1]

if (-not $ManifestPath) {
    $ManifestPath = Join-Path $ReadinessRoot 'release-ops-closure-package-manifest.json'
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Closure package manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$issues = New-Object System.Collections.Generic.List[string]

$requiredTopLevel = @('manifestVersion', 'generatedAtUtc', 'tagName', 'releaseVersion', 'linkedArtifactCount', 'missingRequiredCount', 'linkedArtifacts', 'missingRequiredArtifacts')
foreach ($propertyName in $requiredTopLevel) {
    if ($manifest.PSObject.Properties.Match($propertyName).Count -eq 0) {
        $issues.Add("Missing required manifest property: $propertyName") | Out-Null
    }
}

if ($manifest.PSObject.Properties.Match('tagName').Count -gt 0 -and [string]$manifest.tagName -ne $TagName) {
    $issues.Add("Manifest tagName '$($manifest.tagName)' does not match build tag '$TagName'.") | Out-Null
}

if ($manifest.PSObject.Properties.Match('releaseVersion').Count -gt 0 -and [string]$manifest.releaseVersion -ne $expectedVersion) {
    $issues.Add("Manifest releaseVersion '$($manifest.releaseVersion)' does not match expected '$expectedVersion'.") | Out-Null
}

$linkedArtifacts = @()
if ($manifest.PSObject.Properties.Match('linkedArtifacts').Count -gt 0 -and $manifest.linkedArtifacts) {
    $linkedArtifacts = @($manifest.linkedArtifacts)
}

$requiredArtifactNames = @(
    'signoffRecord',
    'evidenceIndex',
    'tagReadinessSummary',
    'tagReadinessHistoryIndex',
    'promotionGateReport',
    'promotionGateTrendIndex'
)

foreach ($requiredName in $requiredArtifactNames) {
    $matchCount = @($linkedArtifacts | Where-Object { [string]$_.name -eq $requiredName }).Count
    if ($matchCount -eq 0) {
        $issues.Add("Missing required linked artifact entry: $requiredName") | Out-Null
    }
    elseif ($matchCount -gt 1) {
        $issues.Add("Duplicate linked artifact entries found for: $requiredName") | Out-Null
    }
}

foreach ($artifact in $linkedArtifacts) {
    if ([string]::IsNullOrWhiteSpace([string]$artifact.path)) {
        $issues.Add("Linked artifact '$([string]$artifact.name)' has empty path.") | Out-Null
        continue
    }

    $isRequired = [bool]$artifact.required
    $existsInManifest = [bool]$artifact.exists

    $absolutePath = Join-Path $repoRoot ([string]$artifact.path)
    $existsOnDisk = Test-Path -LiteralPath $absolutePath -PathType Leaf

    if ($existsInManifest -ne $existsOnDisk) {
        $issues.Add("Linked artifact '$([string]$artifact.name)' has stale exists flag (manifest=$existsInManifest, disk=$existsOnDisk): $([string]$artifact.path)") | Out-Null
    }

    if ($isRequired -and -not $existsOnDisk) {
        $issues.Add("Required linked artifact missing on disk: $([string]$artifact.name) :: $([string]$artifact.path)") | Out-Null
    }
}

if ($manifest.PSObject.Properties.Match('missingRequiredCount').Count -gt 0 -and [int]$manifest.missingRequiredCount -ne 0) {
    $issues.Add("missingRequiredCount must be 0, found: $([int]$manifest.missingRequiredCount)") | Out-Null
}

$missingList = @()
if ($manifest.PSObject.Properties.Match('missingRequiredArtifacts').Count -gt 0 -and $manifest.missingRequiredArtifacts) {
    $missingList = @($manifest.missingRequiredArtifacts)
}
if ($missingList.Count -ne 0) {
    $issues.Add("missingRequiredArtifacts must be empty, found $($missingList.Count) entr$(if ($missingList.Count -eq 1) { 'y' } else { 'ies' }).") | Out-Null
}

if ($issues.Count -gt 0) {
    Write-Host "Closure package manifest validation failed: $ManifestPath"
    foreach ($issue in $issues) {
        Write-Host " - $issue"
    }

    throw "Closure package manifest validation failed with $($issues.Count) issue(s)."
}

Write-Host "Closure package manifest validation passed for tag $TagName using $ManifestPath."
