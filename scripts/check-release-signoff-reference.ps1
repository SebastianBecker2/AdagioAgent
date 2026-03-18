Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$isTaggedBuild = ($env:APPVEYOR_REPO_TAG -eq 'true') -or ($env:GITHUB_REF_TYPE -eq 'tag')
$tagName = if ($env:APPVEYOR_REPO_TAG_NAME) { $env:APPVEYOR_REPO_TAG_NAME } elseif ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { '' }

if (-not $isTaggedBuild -or [string]::IsNullOrWhiteSpace($tagName)) {
    Write-Host 'Release sign-off reference check skipped (not a tagged build).'
    exit 0
}

if ($tagName -notmatch '^v(\d+\.\d+\.\d+)$') {
    throw "Tag '$tagName' is not SemVer formatted as v<major.minor.patch>."
}

$version = $Matches[1]
$signoffDir = Join-Path $repoRoot 'docs\release-ops\signoffs'
if (-not (Test-Path -LiteralPath $signoffDir -PathType Container)) {
    throw 'Sign-off directory missing: docs/release-ops/signoffs'
}

$matches = Get-ChildItem -LiteralPath $signoffDir -File -Filter "v$version-*.md"
if (-not $matches -or $matches.Count -eq 0) {
    throw "No sign-off record found for release $version in docs/release-ops/signoffs."
}

$latest = $matches | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$relative = "docs/release-ops/signoffs/$($latest.Name)"
$changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
if (-not (Test-Path -LiteralPath $changelogPath -PathType Leaf)) {
    throw 'CHANGELOG.md missing while checking sign-off reference.'
}

$changelog = Get-Content -LiteralPath $changelogPath -Raw
if ($changelog -notmatch [regex]::Escape($relative)) {
    throw "CHANGELOG.md must reference sign-off record for tagged release: $relative"
}

Write-Host "Release sign-off reference check passed for tag $tagName using $relative"
