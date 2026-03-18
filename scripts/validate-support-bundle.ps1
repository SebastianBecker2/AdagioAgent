param(
    [Parameter(Mandatory = $true)]
    [string]$BundlePath,
    [switch]$ExpectOffline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $BundlePath -PathType Container)) {
    throw "Bundle path does not exist: $BundlePath"
}

$manifestPath = Join-Path $BundlePath 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "manifest.json not found in bundle: $BundlePath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$requiredTopLevel = @(
    'GeneratedAtUtc',
    'BundleDirectory',
    'ApiBaseUrl',
    'OfflineMode',
    'ArtifactSchema',
    'IncludedFiles',
    'Notes'
)

foreach ($field in $requiredTopLevel) {
    if (-not ($manifest.PSObject.Properties.Name -contains $field)) {
        throw "manifest.json missing top-level field: $field"
    }
}

if (-not ($manifest.ArtifactSchema.PSObject.Properties.Name -contains 'RequiredArtifacts')) {
    throw 'manifest.json ArtifactSchema is missing RequiredArtifacts.'
}

if (-not ($manifest.ArtifactSchema.PSObject.Properties.Name -contains 'OptionalArtifacts')) {
    throw 'manifest.json ArtifactSchema is missing OptionalArtifacts.'
}

if (-not ($manifest.IncludedFiles -contains 'machine-info.json')) {
    throw 'IncludedFiles missing machine-info.json'
}

if (-not ($manifest.IncludedFiles -contains 'service-status.json')) {
    throw 'IncludedFiles missing service-status.json'
}

if (-not ($manifest.IncludedFiles -contains 'application-events.json')) {
    throw 'IncludedFiles missing application-events.json'
}

if ($ExpectOffline) {
    if (-not [bool]$manifest.OfflineMode) {
        throw 'Expected OfflineMode=true, but manifest indicates online collection.'
    }

    if (-not ($manifest.IncludedFiles -contains 'offline-note.txt')) {
        throw 'Expected offline-note.txt for offline bundle validation.'
    }

    if ($manifest.IncludedFiles -contains 'health.json') {
        throw 'Offline bundle should not include health.json.'
    }

    if ($manifest.IncludedFiles -contains 'ready.json') {
        throw 'Offline bundle should not include ready.json.'
    }
}

$mustExistInBundle = @(
    'machine-info.json',
    'service-status.json',
    'application-events.json'
)

foreach ($file in $mustExistInBundle) {
    $fullPath = Join-Path $BundlePath $file
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Expected artifact missing from bundle directory: $file"
    }
}

Write-Host "Support bundle validation passed: $BundlePath"
