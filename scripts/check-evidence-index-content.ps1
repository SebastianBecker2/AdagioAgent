Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-ApprovedExternalEvidenceUri {
    param([string]$Value)

    if ($Value -match '^https://') { return $true }
    if ($Value -match '^s3://') { return $true }
    if ($Value -match '^gs://') { return $true }
    if ($Value -match '^az://') { return $true }
    if ($Value -match '^\\\\[^\\]+\\[^\\]+') { return $true }

    return $false
}

function Test-RepoRelativeEvidencePath {
    param(
        [string]$Value,
        [string]$ExpectedPrefix
    )

    if ($Value -match '^[a-zA-Z]:[\\/]') { return $false }
    if ($Value -match '^/') { return $false }
    if ($Value -match '^\\(?!\\)') { return $false }
    if ($Value -match '(^|[\\/])\.\.([\\/]|$)') { return $false }

    $normalized = $Value -replace '\\', '/'
    if (-not $normalized.StartsWith($ExpectedPrefix)) { return $false }

    return $true
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$isTaggedBuild = ($env:APPVEYOR_REPO_TAG -eq 'true') -or ($env:GITHUB_REF_TYPE -eq 'tag')
$tagName = if ($env:APPVEYOR_REPO_TAG_NAME) { $env:APPVEYOR_REPO_TAG_NAME } elseif ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { '' }

if (-not $isTaggedBuild -or [string]::IsNullOrWhiteSpace($tagName)) {
    Write-Host 'Evidence index content check skipped (not a tagged build).'
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

$signoffMatches = @(Get-ChildItem -LiteralPath $signoffDir -File -Filter "v$version-*.md")
if (-not $signoffMatches -or $signoffMatches.Count -eq 0) {
    throw "No sign-off record found for release $version in docs/release-ops/signoffs."
}

$signoffFile = $signoffMatches | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$signoffContent = Get-Content -LiteralPath $signoffFile.FullName -Raw
$indexPathMatch = [regex]::Match($signoffContent, '(?m)^-\s*Evidence index path:\s*(.+)$')
if (-not $indexPathMatch.Success) {
    throw "Sign-off record missing evidence index path: $($signoffFile.Name)"
}

$indexPath = $indexPathMatch.Groups[1].Value.Trim()
if ([string]::IsNullOrWhiteSpace($indexPath) -or $indexPath -match '^(TBD|TODO|N/A|<.+>)$') {
    throw "Sign-off evidence index path is empty or placeholder: $($signoffFile.Name)"
}

$indexNormalized = $indexPath -replace '\\', '/'
if (-not $indexNormalized.StartsWith('docs/release-ops/evidence/indexes/')) {
    throw "Evidence index path must be repo-relative under docs/release-ops/evidence/indexes/: $indexPath"
}

$indexAbsolute = Join-Path $repoRoot $indexPath
if (-not (Test-Path -LiteralPath $indexAbsolute -PathType Leaf)) {
    throw "Referenced evidence index does not exist: $indexPath"
}

$indexContent = Get-Content -LiteralPath $indexAbsolute -Raw
$requiredEntries = @(
    @{ Label = 'Support bundle:'; Prefix = 'docs/release-ops/evidence/support-bundles/' },
    @{ Label = 'Correlation trace:'; Prefix = 'docs/release-ops/evidence/correlation-traces/' },
    @{ Label = 'Rollback rehearsal:'; Prefix = 'docs/release-ops/evidence/rollback/' },
    @{ Label = 'Upgrade validation:'; Prefix = 'docs/release-ops/evidence/upgrade-validation/' }
)

$issues = New-Object System.Collections.Generic.List[string]

foreach ($entry in $requiredEntries) {
    $pattern = '(?m)^-\s*' + [regex]::Escape($entry.Label) + '\s*(.+)$'
    $entryMatch = [regex]::Match($indexContent, $pattern)
    if (-not $entryMatch.Success) {
        $issues.Add("Missing required evidence index entry: $($entry.Label)")
        continue
    }

    $value = $entryMatch.Groups[1].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        $issues.Add("Evidence index value is empty for '$($entry.Label)'.")
        continue
    }

    if ($value -match '^(TBD|TODO|N/A|<.+>)$') {
        $issues.Add("Evidence index value is placeholder for '$($entry.Label)'.")
        continue
    }

    if ($value -match '^http://') {
        $issues.Add("Unapproved external URI format for '$($entry.Label)': use https:// if web-hosted.")
        continue
    }

    if (Test-ApprovedExternalEvidenceUri -Value $value) {
        continue
    }

    if (-not (Test-RepoRelativeEvidencePath -Value $value -ExpectedPrefix $entry.Prefix)) {
        $issues.Add("Evidence index value for '$($entry.Label)' must be repo-relative under '$($entry.Prefix)' or use an approved external URI format.")
        continue
    }

    $normalizedValue = $value -replace '\\', '/'
    if ($normalizedValue -notmatch "^docs/release-ops/evidence/.*/v$([regex]::Escape($version))-") {
        $issues.Add("Evidence index value for '$($entry.Label)' must be version-scoped with prefix v$version-.")
    }

    $candidatePath = Join-Path $repoRoot $value
    if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
        $issues.Add("Evidence index path does not exist for '$($entry.Label)': $value")
    }
}

if ($issues.Count -gt 0) {
    Write-Host "Evidence index content check failed for $($indexPath):"
    foreach ($issue in $issues) {
        Write-Host " - $issue"
    }

    throw "Evidence index content check failed with $($issues.Count) issue(s)."
}

Write-Host "Evidence index content check passed for tag $tagName using $indexPath."