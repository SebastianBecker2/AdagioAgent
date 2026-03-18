param(
    [string]$ReadinessRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-tag-readiness'),
    [string]$OutputDir,
    [string]$ManifestPath,
    [string]$TagName,
    [switch]$FailOnIssues
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
    Write-Host 'Closure package integrity report skipped (not a tagged build).'
    return
}

if ($TagName -notmatch '^v(\d+\.\d+\.\d+)$') {
    throw "Tag '$TagName' is not SemVer formatted as v<major.minor.patch>."
}

if (-not $OutputDir) {
    $OutputDir = $ReadinessRoot
}

if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

if (-not $ManifestPath) {
    $ManifestPath = Join-Path $ReadinessRoot 'release-ops-closure-package-manifest.json'
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Closure package manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$linkedArtifacts = @()
if ($manifest.PSObject.Properties.Match('linkedArtifacts').Count -gt 0 -and $manifest.linkedArtifacts) {
    $linkedArtifacts = @($manifest.linkedArtifacts)
}

$issues = New-Object System.Collections.Generic.List[string]
if ($linkedArtifacts.Count -eq 0) {
    $issues.Add('Manifest linkedArtifacts list is empty.') | Out-Null
}

$verifiedArtifacts = New-Object System.Collections.Generic.List[object]

foreach ($artifact in $linkedArtifacts) {
    $artifactName = [string]$artifact.name
    $artifactPath = [string]$artifact.path
    $existsInManifest = [bool]$artifact.exists
    $required = [bool]$artifact.required

    if ([string]::IsNullOrWhiteSpace($artifactPath)) {
        $issues.Add("Linked artifact '$artifactName' has empty path.") | Out-Null
        continue
    }

    $absolutePath = Join-Path $repoRoot $artifactPath
    $existsOnDisk = Test-Path -LiteralPath $absolutePath -PathType Leaf

    if ($existsInManifest -ne $existsOnDisk) {
        $issues.Add("Linked artifact '$artifactName' has stale exists flag (manifest=$existsInManifest, disk=$existsOnDisk): $artifactPath") | Out-Null
    }

    if ($required -and -not $existsOnDisk) {
        $issues.Add("Required linked artifact missing on disk: $artifactName :: $artifactPath") | Out-Null
    }

    $sha256 = ''
    $lastWriteUtc = ''
    $status = if ($existsOnDisk) { 'present' } else { 'missing' }

    if ($existsOnDisk) {
        $sha256 = ([string](Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash).ToLowerInvariant()
        $lastWriteUtc = [DateTimeOffset](Get-Item -LiteralPath $absolutePath).LastWriteTimeUtc | ForEach-Object { $_.ToString('o') }
    }

    $verifiedArtifacts.Add([pscustomobject]@{
        name = $artifactName
        path = $artifactPath
        required = $required
        existsInManifest = $existsInManifest
        existsOnDisk = $existsOnDisk
        sha256 = $sha256
        lastWriteTimeUtc = $lastWriteUtc
        status = $status
    }) | Out-Null
}

$manifestSha = ([string](Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash).ToLowerInvariant()
$integrityVerdict = if ($issues.Count -eq 0) { 'pass' } else { 'fail' }

$reportObject = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    tagName = $TagName
    integrityVerdict = $integrityVerdict
    issueCount = $issues.Count
    issues = $issues.ToArray()
    manifest = [pscustomobject]@{
        path = $ManifestPath
        sha256 = $manifestSha
    }
    verifiedArtifactCount = $verifiedArtifacts.Count
    verifiedArtifacts = $verifiedArtifacts.ToArray()
}

$outputJsonPath = Join-Path $OutputDir 'release-ops-closure-package-integrity-report.json'
$outputMdPath = Join-Path $OutputDir 'release-ops-closure-package-integrity-report.md'

$reportObject | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputJsonPath -Encoding UTF8

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add('# Release-Ops Closure Package Integrity Report') | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add("- GeneratedAtUtc: $($reportObject.generatedAtUtc)") | Out-Null
$mdLines.Add("- TagName: $TagName") | Out-Null
$mdLines.Add("- IntegrityVerdict: **$($integrityVerdict.ToUpper())**") | Out-Null
$mdLines.Add("- IssueCount: $($issues.Count)") | Out-Null
$mdLines.Add("- ManifestPath: $ManifestPath") | Out-Null
$mdLines.Add("- ManifestSha256: $manifestSha") | Out-Null
$mdLines.Add('') | Out-Null
$mdLines.Add('## Linked Artifact Integrity') | Out-Null
$mdLines.Add('') | Out-Null

foreach ($item in $verifiedArtifacts) {
    $mdLines.Add("- [$($item.status.ToUpper())] $($item.name): $($item.path)") | Out-Null
    if ($item.existsOnDisk) {
        $mdLines.Add("  - SHA256: $($item.sha256)") | Out-Null
        $mdLines.Add("  - LastWriteUtc: $($item.lastWriteTimeUtc)") | Out-Null
    }
}

if ($issues.Count -gt 0) {
    $mdLines.Add('') | Out-Null
    $mdLines.Add('## Issues') | Out-Null
    $mdLines.Add('') | Out-Null
    foreach ($issue in $issues) {
        $mdLines.Add("- $issue") | Out-Null
    }
}

Set-Content -LiteralPath $outputMdPath -Value ($mdLines -join "`n") -Encoding UTF8

Write-Host "Release-ops closure package integrity report written: $outputJsonPath (integrityVerdict=$integrityVerdict)"

if ($FailOnIssues -and $issues.Count -gt 0) {
    throw "Closure package integrity report detected $($issues.Count) issue(s)."
}

