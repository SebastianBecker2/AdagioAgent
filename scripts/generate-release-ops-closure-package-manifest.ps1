param(
    [string]$ReadinessRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [string]$OutputDir,
    [string]$TagName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$repoRootPath = [string]$repoRoot

function Get-RepoRelativePath {
    param(
        [string]$AbsolutePath,
        [string]$BasePath
    )

    $baseUri = New-Object System.Uri(($BasePath.TrimEnd('\') + '\'))
    $targetUri = New-Object System.Uri($AbsolutePath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()) -replace '/', '/'
}

if (-not $OutputDir) {
    $OutputDir = $ReadinessRoot
}

if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

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

if ([string]::IsNullOrWhiteSpace($TagName)) {
    throw 'TagName is required. Provide -TagName or run in a tagged build environment.'
}

if ($TagName -notmatch '^v(\d+\.\d+\.\d+)$') {
    throw "Tag '$TagName' is not SemVer formatted as v<major.minor.patch>."
}

$version = $Matches[1]

$signoffDir = Join-Path $repoRoot 'docs\release-ops\signoffs'
if (-not (Test-Path -LiteralPath $signoffDir -PathType Container)) {
    throw 'Sign-off directory missing: docs/release-ops/signoffs'
}

$signoffMatches = @(Get-ChildItem -LiteralPath $signoffDir -File -Filter "v$version-*.md")
if ($signoffMatches.Count -eq 0) {
    throw "No sign-off record found for release $version in docs/release-ops/signoffs."
}

$signoffFile = $signoffMatches | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$signoffRelativePath = "docs/release-ops/signoffs/$($signoffFile.Name)"
$signoffContent = Get-Content -LiteralPath $signoffFile.FullName -Raw

$indexMatch = [regex]::Match($signoffContent, '(?m)^-\s*Evidence index path:\s*(.+)$')
if (-not $indexMatch.Success) {
    throw "Sign-off record missing 'Evidence index path' entry: $($signoffFile.Name)"
}

$evidenceIndexPath = $indexMatch.Groups[1].Value.Trim()
if ([string]::IsNullOrWhiteSpace($evidenceIndexPath) -or $evidenceIndexPath -match '^(TBD|TODO|N/A|<.+>)$') {
    throw "Sign-off record has empty/placeholder evidence index path: $($signoffFile.Name)"
}

$resolvedReadinessRoot = Resolve-Path -LiteralPath $ReadinessRoot
$readinessRootPath = [string]$resolvedReadinessRoot

$tagReadinessSummaryAbsolute = Join-Path $readinessRootPath 'release-ops-tag-readiness-summary.json'
$tagReadinessHistoryAbsolute = Join-Path $readinessRootPath 'release-ops-tag-readiness-history-index.json'
$promotionGateReportAbsolute = Join-Path $readinessRootPath 'release-ops-promotion-gate-report.json'
$promotionGateTrendAbsolute = Join-Path $readinessRootPath 'release-ops-promotion-gate-trend-index.json'

$tagReadinessSummaryPath = Get-RepoRelativePath -AbsolutePath $tagReadinessSummaryAbsolute -BasePath $repoRootPath
$tagReadinessHistoryPath = Get-RepoRelativePath -AbsolutePath $tagReadinessHistoryAbsolute -BasePath $repoRootPath
$promotionGateReportPath = Get-RepoRelativePath -AbsolutePath $promotionGateReportAbsolute -BasePath $repoRootPath
$promotionGateTrendPath = Get-RepoRelativePath -AbsolutePath $promotionGateTrendAbsolute -BasePath $repoRootPath

$artifactEntries = @(
    [pscustomobject]@{ name = 'signoffRecord'; category = 'release-signoff'; path = $signoffRelativePath; required = $true; retention = 'Keep in-repo for at least one full release cycle after closure.' },
    [pscustomobject]@{ name = 'evidenceIndex'; category = 'release-evidence'; path = $evidenceIndexPath; required = $true; retention = 'Keep in-repo and update referenced archive URIs after retention window transitions.' },
    [pscustomobject]@{ name = 'tagReadinessSummary'; category = 'release-readiness'; path = $tagReadinessSummaryPath; required = $true; retention = 'Retain with tagged release evidence for auditability.' },
    [pscustomobject]@{ name = 'tagReadinessHistoryIndex'; category = 'release-readiness'; path = $tagReadinessHistoryPath; required = $true; retention = 'Retain trend index for release process audits.' },
    [pscustomobject]@{ name = 'promotionGateReport'; category = 'promotion-gate'; path = $promotionGateReportPath; required = $true; retention = 'Retain verdict evidence for release approval decisions.' },
    [pscustomobject]@{ name = 'promotionGateTrendIndex'; category = 'promotion-gate'; path = $promotionGateTrendPath; required = $true; retention = 'Retain trend/override history for governance review.' }
)

$linkedArtifacts = New-Object System.Collections.Generic.List[object]
$missingRequired = New-Object System.Collections.Generic.List[string]

foreach ($entry in $artifactEntries) {
    $candidate = Join-Path $repoRoot $entry.path
    $exists = Test-Path -LiteralPath $candidate -PathType Leaf

    if ($entry.required -and -not $exists) {
        $missingRequired.Add("$($entry.name): $($entry.path)") | Out-Null
    }

    $linkedArtifacts.Add([pscustomobject]@{
        name = $entry.name
        category = $entry.category
        path = $entry.path
        exists = $exists
        required = [bool]$entry.required
        retentionExpectation = $entry.retention
    }) | Out-Null
}

$manifest = [pscustomobject]@{
    manifestVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    tagName = $TagName
    releaseVersion = $version
    closureScope = 'release-ops-tagged-release'
    linkedArtifactCount = $linkedArtifacts.Count
    missingRequiredCount = $missingRequired.Count
    linkedArtifacts = $linkedArtifacts.ToArray()
    missingRequiredArtifacts = $missingRequired.ToArray()
}

$manifestJsonPath = Join-Path $OutputDir 'release-ops-closure-package-manifest.json'
$manifestMdPath = Join-Path $OutputDir 'release-ops-closure-package-manifest.md'

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops Closure Package Manifest') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($manifest.generatedAtUtc)") | Out-Null
$mdLines.Add("- TagName: $TagName") | Out-Null
$mdLines.Add("- ReleaseVersion: $version") | Out-Null
$mdLines.Add("- LinkedArtifactCount: $($manifest.linkedArtifactCount)") | Out-Null
$mdLines.Add("- MissingRequiredCount: $($manifest.missingRequiredCount)") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Linked Artifacts') | Out-Null
$mdLines.Add('') | Out-Null

foreach ($artifact in $manifest.linkedArtifacts) {
    $status = if ($artifact.exists) { 'FOUND' } else { 'MISSING' }
    $required = if ($artifact.required) { 'required' } else { 'optional' }
    $mdLines.Add("- [$status][$required] $($artifact.name) :: $($artifact.path)") | Out-Null
}

if ($manifest.missingRequiredCount -gt 0) {
    $mdLines.Add('') | Out-Null
    $mdLines.Add('## Missing Required Artifacts') | Out-Null
    $mdLines.Add('') | Out-Null
    foreach ($missing in $manifest.missingRequiredArtifacts) {
        $mdLines.Add("- $missing") | Out-Null
    }
}

Set-Content -LiteralPath $manifestMdPath -Value ($mdLines -join "`n") -Encoding UTF8
Write-Host "Release-ops closure package manifest written: $manifestJsonPath"

if ($manifest.missingRequiredCount -gt 0) {
    throw "Closure package manifest is missing $($manifest.missingRequiredCount) required artifact(s). See $manifestJsonPath"
}
