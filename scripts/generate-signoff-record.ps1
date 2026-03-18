param(
    [string]$Version,
    [string]$TemplatePath = (Join-Path $PSScriptRoot '..\docs\OPERATIONS_SIGNOFF_TEMPLATE.md'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\docs\release-ops\signoffs'),
    [string]$DateUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'),
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

if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) {
    throw "Sign-off template not found: $TemplatePath"
}

if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$dateStamp = ($DateUtc -replace '-', '')
$fileName = "v$Version-$dateStamp.md"
$outputPath = Join-Path $OutputDirectory $fileName

if ((Test-Path -LiteralPath $outputPath -PathType Leaf) -and -not $Force) {
    throw "Sign-off record already exists: $outputPath (use -Force to overwrite)"
}

$template = Get-Content -LiteralPath $TemplatePath -Raw
$header = @(
    "# Release Ops Sign-Off Record",
    "",
    "- Version: $Version",
    "- GeneratedAtUtc: $([DateTimeOffset]::UtcNow.ToString('u'))",
    "- SourceTemplate: docs/OPERATIONS_SIGNOFF_TEMPLATE.md",
    "",
    "---",
    ""
) -join "`n"

$suggestedEvidenceIndex = "docs/release-ops/evidence/indexes/v$Version-$dateStamp-evidence.md"
$body = $template -replace '(?m)^- Release version:\s*$', "- Release version: $Version" -replace '(?m)^- Sign-off date \(UTC\):\s*$', "- Sign-off date (UTC): $DateUtc" -replace '(?m)^- Evidence index path:\s*$', "- Evidence index path: $suggestedEvidenceIndex"
Set-Content -LiteralPath $outputPath -Value ($header + $body) -Encoding UTF8

$relativeOutput = Resolve-Path -LiteralPath $outputPath | ForEach-Object { $_.Path.Replace((Resolve-Path (Join-Path $PSScriptRoot '..')).Path + '\\', '') }
Write-Host "Generated sign-off record: $relativeOutput"
