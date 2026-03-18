param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-dry-run'),
    [string]$PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$issues = New-Object System.Collections.Generic.List[string]

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    if (-not (Test-Path -LiteralPath $OutputRoot -PathType Container)) {
        throw "Dry-run output root not found: $OutputRoot"
    }

    $latest = Get-ChildItem -LiteralPath $OutputRoot -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) {
        throw "No dry-run package directories found under: $OutputRoot"
    }

    $PackagePath = $latest.FullName
}

if (-not (Test-Path -LiteralPath $PackagePath -PathType Container)) {
    throw "Dry-run package directory not found: $PackagePath"
}

$requiredRelativeFiles = @(
    'manifest.json',
    'signoffs',
    'evidence/indexes',
    'evidence/support-bundles',
    'evidence/correlation-traces',
    'evidence/rollback',
    'evidence/upgrade-validation'
)

foreach ($relative in $requiredRelativeFiles) {
    $path = Join-Path $PackagePath $relative
    if (-not (Test-Path -LiteralPath $path)) {
        $issues.Add("Missing required package path: $relative")
    }
}

if ($issues.Count -gt 0) {
    foreach ($issue in $issues) { Write-Host " - $issue" }
    throw "Dry-run package validation failed with $($issues.Count) structural issue(s)."
}

$manifestPath = Join-Path $PackagePath 'manifest.json'
$manifestRaw = Get-Content -LiteralPath $manifestPath -Raw
$manifest = $null
try {
    $manifest = $manifestRaw | ConvertFrom-Json
}
catch {
    throw "manifest.json is not valid JSON: $($_.Exception.Message)"
}

$requiredManifestFields = @('version', 'dateStamp', 'signoffPath', 'evidenceIndexPath', 'fixturePaths')
foreach ($field in $requiredManifestFields) {
    $value = $manifest.$field
    if ($null -eq $value -or ([string]::IsNullOrWhiteSpace([string]$value) -and $field -ne 'fixturePaths')) {
        $issues.Add("manifest.json missing required field: $field")
    }
}

if ($manifest.version -and $manifest.version -notmatch '^\d+\.\d+\.\d+$') {
    $issues.Add("manifest.json field 'version' is not SemVer: $($manifest.version)")
}

if ($manifest.dateStamp -and $manifest.dateStamp -notmatch '^\d{8}$') {
    $issues.Add("manifest.json field 'dateStamp' must be yyyymmdd: $($manifest.dateStamp)")
}

if ($manifest.fixturePaths -and $manifest.fixturePaths.Count -lt 4) {
    $issues.Add('manifest.json field fixturePaths must contain at least four entries.')
}

$signoffPath = if ($manifest.signoffPath) { Join-Path $PackagePath $manifest.signoffPath } else { $null }
$indexPath = if ($manifest.evidenceIndexPath) { Join-Path $PackagePath $manifest.evidenceIndexPath } else { $null }

if ($signoffPath -and -not (Test-Path -LiteralPath $signoffPath -PathType Leaf)) {
    $issues.Add("Sign-off file referenced by manifest does not exist: $($manifest.signoffPath)")
}

if ($indexPath -and -not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    $issues.Add("Evidence index file referenced by manifest does not exist: $($manifest.evidenceIndexPath)")
}

if ($signoffPath -and (Test-Path -LiteralPath $signoffPath -PathType Leaf)) {
    $signoffContent = Get-Content -LiteralPath $signoffPath -Raw
    if ($signoffContent -notmatch '(?m)^-\s*Evidence index path:\s*(.+)$') {
        $issues.Add("Sign-off file missing 'Evidence index path' field: $($manifest.signoffPath)")
    }
}

if ($indexPath -and (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    $indexContent = Get-Content -LiteralPath $indexPath -Raw

    $requiredIndexLabels = @('Support bundle:', 'Correlation trace:', 'Rollback rehearsal:', 'Upgrade validation:')
    foreach ($label in $requiredIndexLabels) {
        if ($indexContent -notmatch ('(?m)^-\s*' + [regex]::Escape($label) + '\s*(.+)$')) {
            $issues.Add("Evidence index missing required label: $label")
        }
    }

    if ($manifest.signoffPath) {
        $expectedSignoff = '- SignOffRecord: ' + ($manifest.signoffPath -replace '\\', '/')
        if ($indexContent -notmatch [regex]::Escape($expectedSignoff)) {
            $issues.Add("Evidence index SignOffRecord does not match manifest signoffPath: $($manifest.signoffPath)")
        }
    }
}

if ($manifest.fixturePaths) {
    foreach ($fixtureRelative in $manifest.fixturePaths) {
        if ([string]::IsNullOrWhiteSpace([string]$fixtureRelative)) {
            $issues.Add('manifest.json fixturePaths contains an empty path entry.')
            continue
        }

        $fixturePath = Join-Path $PackagePath $fixtureRelative
        if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) {
            $issues.Add("Fixture file referenced by manifest does not exist: $fixtureRelative")
        }
    }
}

if ($issues.Count -gt 0) {
    Write-Host "Dry-run package validation failed for: $PackagePath"
    foreach ($issue in $issues) {
        Write-Host " - $issue"
    }

    throw "Dry-run package validation failed with $($issues.Count) issue(s)."
}

Write-Host "Dry-run package validation passed for: $PackagePath"
