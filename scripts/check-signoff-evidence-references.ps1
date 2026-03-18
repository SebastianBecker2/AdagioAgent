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
    param([string]$Value)

    if ($Value -match '^[a-zA-Z]:[\\/]') { return $false }
    if ($Value -match '^/') { return $false }
    if ($Value -match '^\\(?!\\)') { return $false }
    if ($Value -match '(^|[\\/])\.\.([\\/]|$)') { return $false }

    $normalized = $Value -replace '\\', '/'
    if (-not $normalized.StartsWith('docs/release-ops/evidence/')) { return $false }

    return $true
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$isTaggedBuild = ($env:APPVEYOR_REPO_TAG -eq 'true') -or ($env:GITHUB_REF_TYPE -eq 'tag')
$tagName = if ($env:APPVEYOR_REPO_TAG_NAME) { $env:APPVEYOR_REPO_TAG_NAME } elseif ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { '' }

if (-not $isTaggedBuild -or [string]::IsNullOrWhiteSpace($tagName)) {
    Write-Host 'Sign-off evidence reference check skipped (not a tagged build).'
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
$content = Get-Content -LiteralPath $signoffFile.FullName -Raw

$requiredLabels = @(
    'Support bundle evidence path:',
    'Correlation trace evidence path:',
    'Rollback rehearsal evidence path:',
    'Upgrade validation evidence path:'
)

$issues = New-Object System.Collections.Generic.List[string]

foreach ($label in $requiredLabels) {
    $pattern = "(?m)^-\s*" + [regex]::Escape($label) + "\s*(.+)$"
    $match = [regex]::Match($content, $pattern)
    if (-not $match.Success) {
        $issues.Add("Missing evidence label in sign-off record: $label")
        continue
    }

    $value = $match.Groups[1].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        $issues.Add("Evidence value not set for label: $label")
        continue
    }

    $looksLikePlaceholder = $value -match '^(TBD|TODO|N/A|<.+>)$'
    if ($looksLikePlaceholder) {
        $issues.Add("Evidence value is placeholder for label: $label")
        continue
    }

    if ($value -match '^http://') {
        $issues.Add("Unapproved external URI format for label '$label': use https:// if web-hosted.")
        continue
    }

    if (Test-ApprovedExternalEvidenceUri -Value $value) {
        # Approved external evidence URI formats are allowed.
        continue
    }

    if (-not (Test-RepoRelativeEvidencePath -Value $value)) {
        $issues.Add("Evidence value for label '$label' must be repo-relative under docs/release-ops/evidence/ or use an approved external URI format (https://, s3://, gs://, az://, \\\\UNC).")
        continue
    }

    $candidatePath = Join-Path $repoRoot $value
    if (-not (Test-Path -LiteralPath $candidatePath)) {
        $issues.Add("Evidence path does not exist: $value")
    }
}

if ($issues.Count -gt 0) {
    Write-Host "Sign-off evidence reference check failed for $($signoffFile.Name):"
    foreach ($issue in $issues) {
        Write-Host " - $issue"
    }

    throw "Sign-off evidence reference check failed with $($issues.Count) issue(s)."
}

Write-Host "Sign-off evidence reference check passed for $($signoffFile.Name)."
