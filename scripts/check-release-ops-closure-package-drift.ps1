param(
    [string]$ReadinessRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [string]$ManifestPath,
    [string]$TagName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$repoRootPath = [System.IO.Path]::GetFullPath([string]$repoRoot.ProviderPath)

function Get-RepoRelativePath {
    param(
        [string]$AbsolutePath,
        [string]$BasePath
    )

    $baseFullPath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\')
    $targetFullPath = [System.IO.Path]::GetFullPath($AbsolutePath)

    if ($targetFullPath.StartsWith($baseFullPath + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        $relativePath = $targetFullPath.Substring($baseFullPath.Length + 1)
        return $relativePath -replace '\\', '/'
    }

    throw "Path '$targetFullPath' is not under base path '$baseFullPath'."
}

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
    Write-Host 'Closure package drift check skipped (not a tagged build).'
    exit 0
}

if ($TagName -notmatch '^v(\d+\.\d+\.\d+)$') {
    throw "Tag '$TagName' is not SemVer formatted as v<major.minor.patch>."
}

if (-not $ManifestPath) {
    $ManifestPath = Join-Path $ReadinessRoot 'release-ops-closure-package-manifest.json'
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Closure package manifest not found for drift check: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$linkedArtifacts = @()
if ($manifest.PSObject.Properties.Match('linkedArtifacts').Count -gt 0 -and $manifest.linkedArtifacts) {
    $linkedArtifacts = @($manifest.linkedArtifacts)
}

$manifestGeneratedAt = $null
try {
    $manifestGeneratedAt = [DateTimeOffset]::Parse([string]$manifest.generatedAtUtc)
}
catch {
    $manifestGeneratedAt = [DateTimeOffset](Get-Item -LiteralPath $ManifestPath).LastWriteTimeUtc
}

$resolvedReadinessRoot = Resolve-Path -LiteralPath $ReadinessRoot
$readinessRootPath = [string]$resolvedReadinessRoot

$expectedMap = @{
    tagReadinessSummary = Get-RepoRelativePath -AbsolutePath (Join-Path $readinessRootPath 'release-ops-tag-readiness-summary.json') -BasePath $repoRootPath
    tagReadinessHistoryIndex = Get-RepoRelativePath -AbsolutePath (Join-Path $readinessRootPath 'release-ops-tag-readiness-history-index.json') -BasePath $repoRootPath
    promotionGateReport = Get-RepoRelativePath -AbsolutePath (Join-Path $readinessRootPath 'release-ops-promotion-gate-report.json') -BasePath $repoRootPath
    promotionGateTrendIndex = Get-RepoRelativePath -AbsolutePath (Join-Path $readinessRootPath 'release-ops-promotion-gate-trend-index.json') -BasePath $repoRootPath
}

$issues = New-Object System.Collections.Generic.List[string]

foreach ($name in $expectedMap.Keys) {
    $matches = @($linkedArtifacts | Where-Object { [string]$_.name -eq $name })
    if ($matches.Count -eq 0) {
        $issues.Add("Manifest is missing linked artifact entry: $name") | Out-Null
        continue
    }
    if ($matches.Count -gt 1) {
        $issues.Add("Manifest has duplicate linked artifact entries for: $name") | Out-Null
        continue
    }

    $artifact = $matches[0]
    $expectedPath = [string]$expectedMap[$name]
    $manifestPathValue = [string]$artifact.path

    if ($manifestPathValue -ne $expectedPath) {
        $issues.Add("Drift detected for '$name': manifest path '$manifestPathValue' does not match current expected path '$expectedPath'.") | Out-Null
    }

    $absolutePath = Join-Path $repoRoot $expectedPath
    $existsOnDisk = Test-Path -LiteralPath $absolutePath -PathType Leaf
    $existsInManifest = [bool]$artifact.exists

    if ($existsInManifest -ne $existsOnDisk) {
        $issues.Add("Drift detected for '$name': manifest exists flag=$existsInManifest but on-disk exists=$existsOnDisk ($expectedPath).") | Out-Null
    }

    if ($existsOnDisk) {
        $lastWriteUtc = (Get-Item -LiteralPath $absolutePath).LastWriteTimeUtc
        if ($lastWriteUtc -gt $manifestGeneratedAt.UtcDateTime) {
            $issues.Add("Drift detected for '$name': artifact was modified after manifest generation (artifact=$lastWriteUtc, manifest=$($manifestGeneratedAt.UtcDateTime)).") | Out-Null
        }
    }
}

if ($issues.Count -gt 0) {
    Write-Host "Closure package drift check failed: $ManifestPath"
    foreach ($issue in $issues) {
        Write-Host " - $issue"
    }

    throw "Closure package drift check failed with $($issues.Count) issue(s)."
}

Write-Host "Closure package drift check passed for tag $TagName using $ManifestPath."
