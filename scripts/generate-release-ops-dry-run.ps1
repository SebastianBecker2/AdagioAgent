param(
    [string]$Version,
    [string]$DateUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\release-ops-dry-run'),
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

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$Version' must be SemVer formatted as <major.minor.patch>."
}

$dateStamp = ($DateUtc -replace '-', '')
$packageDir = Join-Path $OutputRoot "v$Version-$dateStamp-dryrun"

if (Test-Path -LiteralPath $packageDir -PathType Container) {
    if (-not $Force) {
        throw "Dry-run package directory already exists: $packageDir (use -Force to overwrite)"
    }

    Remove-Item -LiteralPath $packageDir -Recurse -Force
}

$signoffDir = Join-Path $packageDir 'signoffs'
$evidenceRoot = Join-Path $packageDir 'evidence'
$indexesDir = Join-Path $evidenceRoot 'indexes'
$supportDir = Join-Path $evidenceRoot 'support-bundles'
$traceDir = Join-Path $evidenceRoot 'correlation-traces'
$rollbackDir = Join-Path $evidenceRoot 'rollback'
$upgradeDir = Join-Path $evidenceRoot 'upgrade-validation'

$directories = @(
    $signoffDir,
    $indexesDir,
    $supportDir,
    $traceDir,
    $rollbackDir,
    $upgradeDir
)

foreach ($dir in $directories) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$signoffFileName = "v$Version-$dateStamp.md"
$indexFileName = "v$Version-$dateStamp-evidence.md"
$supportFileName = "v$Version-dryrun-bundle.json"
$traceFileName = "v$Version-dryrun-trace.md"
$rollbackFileName = "v$Version-dryrun-rollback.md"
$upgradeFileName = "v$Version-dryrun-upgrade.md"

$signoffRelative = "signoffs/$signoffFileName"
$indexRelative = "evidence/indexes/$indexFileName"
$supportRelative = "evidence/support-bundles/$supportFileName"
$traceRelative = "evidence/correlation-traces/$traceFileName"
$rollbackRelative = "evidence/rollback/$rollbackFileName"
$upgradeRelative = "evidence/upgrade-validation/$upgradeFileName"

$signoffPath = Join-Path $signoffDir $signoffFileName
$indexPath = Join-Path $indexesDir $indexFileName
$supportPath = Join-Path $supportDir $supportFileName
$tracePath = Join-Path $traceDir $traceFileName
$rollbackPath = Join-Path $rollbackDir $rollbackFileName
$upgradePath = Join-Path $upgradeDir $upgradeFileName

$signoffContent = @(
    '# Release Ops Sign-Off Record (Dry-Run Fixture)',
    '',
    "- Release version: $Version",
    "- Sign-off date (UTC): $DateUtc",
    '- Environment scope: dry-run',
    '',
    "- Evidence index path: $indexRelative",
    "- Support bundle evidence path: $supportRelative",
    "- Correlation trace evidence path: $traceRelative",
    "- Rollback rehearsal evidence path: $rollbackRelative",
    "- Upgrade validation evidence path: $upgradeRelative",
    '',
    '- Notes: Sample fixture package for release-ops dry-run validation.'
) -join "`n"

$indexContent = @(
    "# Evidence Index For v$Version (Dry-Run Fixture)",
    '',
    "- SignOffRecord: $signoffRelative",
    '',
    '## Evidence Paths',
    '',
    "- Support bundle: $supportRelative",
    "- Correlation trace: $traceRelative",
    "- Rollback rehearsal: $rollbackRelative",
    "- Upgrade validation: $upgradeRelative",
    '',
    '## Notes',
    '',
    '- This package is generated for local and CI smoke validation only.'
) -join "`n"

$supportContent = @{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    mode = 'dry-run'
    version = $Version
    artifacts = @(
        @{
            type = 'support-bundle'
            path = $supportRelative
        }
    )
} | ConvertTo-Json -Depth 5

$traceContent = @(
    '# Dry-Run Correlation Trace',
    '',
    "- CorrelationId: dryrun-$Version",
    '- ExtensionMessage: Sample error surfaced with correlation ID.',
    '- BackendLogReference: Sample backend log correlation record.'
) -join "`n"

$rollbackContent = @(
    '# Dry-Run Rollback Rehearsal',
    '',
    "- Version: $Version",
    '- Outcome: Success',
    '- Notes: Simulated rollback rehearsal for dry-run fixture.'
) -join "`n"

$upgradeContent = @(
    '# Dry-Run Upgrade Validation',
    '',
    "- Version: $Version",
    '- Outcome: Success',
    '- Notes: Simulated upgrade validation for dry-run fixture.'
) -join "`n"

$manifestPath = Join-Path $packageDir 'manifest.json'
$manifestContent = @{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    version = $Version
    dateStamp = $dateStamp
    signoffPath = $signoffRelative
    evidenceIndexPath = $indexRelative
    fixturePaths = @(
        $supportRelative,
        $traceRelative,
        $rollbackRelative,
        $upgradeRelative
    )
} | ConvertTo-Json -Depth 5

Set-Content -LiteralPath $signoffPath -Value $signoffContent -Encoding UTF8
Set-Content -LiteralPath $indexPath -Value $indexContent -Encoding UTF8
Set-Content -LiteralPath $supportPath -Value $supportContent -Encoding UTF8
Set-Content -LiteralPath $tracePath -Value $traceContent -Encoding UTF8
Set-Content -LiteralPath $rollbackPath -Value $rollbackContent -Encoding UTF8
Set-Content -LiteralPath $upgradePath -Value $upgradeContent -Encoding UTF8
Set-Content -LiteralPath $manifestPath -Value $manifestContent -Encoding UTF8

Write-Host "Generated release-ops dry-run package: $packageDir"
