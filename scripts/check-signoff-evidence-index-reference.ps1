Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$isTaggedBuild = ($env:APPVEYOR_REPO_TAG -eq 'true') -or ($env:GITHUB_REF_TYPE -eq 'tag')
$tagName = if ($env:APPVEYOR_REPO_TAG_NAME) { $env:APPVEYOR_REPO_TAG_NAME } elseif ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { '' }

if (-not $isTaggedBuild -or [string]::IsNullOrWhiteSpace($tagName)) {
    Write-Host 'Sign-off evidence index reference check skipped (not a tagged build).'
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

$matches = @(Get-ChildItem -LiteralPath $signoffDir -File -Filter "v$version-*.md")
if (-not $matches -or $matches.Count -eq 0) {
    throw "No sign-off record found for release $version in docs/release-ops/signoffs."
}

$signoffFile = $matches | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$signoffRelative = "docs/release-ops/signoffs/$($signoffFile.Name)"
$content = Get-Content -LiteralPath $signoffFile.FullName -Raw

$pattern = '(?m)^-\s*Evidence index path:\s*(.+)$'
$match = [regex]::Match($content, $pattern)
if (-not $match.Success) {
    throw "Missing evidence index reference in sign-off record: $($signoffFile.Name)"
}

$indexPath = $match.Groups[1].Value.Trim()
if ([string]::IsNullOrWhiteSpace($indexPath)) {
    throw "Evidence index path is empty in sign-off record: $($signoffFile.Name)"
}

if ($indexPath -match '^(TBD|TODO|N/A|<.+>)$') {
    throw "Evidence index path is placeholder in sign-off record: $($signoffFile.Name)"
}

$normalized = $indexPath -replace '\\', '/'
if (-not $normalized.StartsWith('docs/release-ops/evidence/indexes/')) {
    throw "Evidence index path must be repo-relative under docs/release-ops/evidence/indexes/: $indexPath"
}

if ($normalized -notmatch "^docs/release-ops/evidence/indexes/v$([regex]::Escape($version))-.+-evidence\.md$") {
    throw "Evidence index path must target release version $version and end with -evidence.md: $indexPath"
}

$indexAbsolute = Join-Path $repoRoot $indexPath
if (-not (Test-Path -LiteralPath $indexAbsolute -PathType Leaf)) {
    throw "Referenced evidence index does not exist: $indexPath"
}

$indexContent = Get-Content -LiteralPath $indexAbsolute -Raw
$expectedSignoffRef = "- SignOffRecord: $signoffRelative"
if ($indexContent -notmatch [regex]::Escape($expectedSignoffRef)) {
    throw "Evidence index must cross-link back to sign-off record via '$expectedSignoffRef'."
}

Write-Host "Sign-off evidence index reference check passed for tag $tagName using $($signoffFile.Name)."