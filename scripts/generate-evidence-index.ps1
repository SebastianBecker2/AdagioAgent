param(
    [string]$Version,
    [string]$DateUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\release-ops\evidence\indexes'),
    [string]$SignoffDirectory = (Join-Path $PSScriptRoot '..\docs\release-ops\signoffs'),
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-DefaultVersion {
    [xml]$project = Get-Content (Join-Path $PSScriptRoot '..\machine-agent\AdagioMachineAgent.csproj')
    $node = $project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
    if (-not $node -or [string]::IsNullOrWhiteSpace($node.Version)) {
        throw 'Could not resolve default version from machine-agent/AdagioMachineAgent.csproj'
    }

    return $node.Version.Trim()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Resolve-DefaultVersion
}

if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

if (-not (Test-Path -LiteralPath $SignoffDirectory -PathType Container)) {
    throw "Sign-off directory not found: $SignoffDirectory"
}

$dateStamp = ($DateUtc -replace '-', '')
$fileName = "v$Version-$dateStamp-evidence.md"
$outputPath = Join-Path $OutputDirectory $fileName

if ((Test-Path -LiteralPath $outputPath -PathType Leaf) -and -not $Force) {
    throw "Evidence index already exists: $outputPath (use -Force to overwrite)"
}

$signoffMatches = @(Get-ChildItem -LiteralPath $SignoffDirectory -File -Filter "v$Version-*.md" | Sort-Object LastWriteTime -Descending)
$signoffReference = if ($signoffMatches -and $signoffMatches.Count -gt 0) {
    "docs/release-ops/signoffs/$($signoffMatches[0].Name)"
} else {
    "docs/release-ops/signoffs/v$Version-<yyyymmdd>.md"
}

$content = @(
    "# Evidence Index For v$Version",
    "",
    "- GeneratedAtUtc: $([DateTimeOffset]::UtcNow.ToString('u'))",
    "- SignOffRecord: $signoffReference",
    "",
    "## Evidence Paths",
    "",
    "- Support bundle: docs/release-ops/evidence/support-bundles/v$Version-<artifact>.json",
    "- Correlation trace: docs/release-ops/evidence/correlation-traces/v$Version-<trace>.md",
    "- Rollback rehearsal: docs/release-ops/evidence/rollback/v$Version-<rehearsal>.md",
    "- Upgrade validation: docs/release-ops/evidence/upgrade-validation/v$Version-<validation>.md",
    "",
    "## Notes",
    "",
    "- Replace placeholders with concrete file names.",
    "- Keep referenced files under release-ops evidence folders when possible.",
    "- For external evidence URIs, document immutable location and access constraints."
) -join "`n"

Set-Content -LiteralPath $outputPath -Value $content -Encoding UTF8
Write-Host "Generated evidence index: $outputPath"
