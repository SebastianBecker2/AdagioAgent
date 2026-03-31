[CmdletBinding()]
param(
    [string]$BundlePath = "installer-bundle\bin\x64\Release\AdagioMachineAgent.Bundle.exe",
    [string]$ResponseFilePath = "",
    [switch]$GenerateResponseFile,
    [string]$ResponseOutputPath = "artifacts\installer\installer-response.json",
    [switch]$DryRun,
    [switch]$LayoutOnly,
    [string]$OutputDir = "artifacts\installer",
    [switch]$PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$responseGeneratorScript = Join-Path $repoRoot 'scripts\generate-installer-response-file.ps1'

function Resolve-RepoRelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Get-TextHash {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ''
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return [System.BitConverter]::ToString($hashBytes).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

$BundlePath = Resolve-RepoRelativePath -Path $BundlePath
$OutputDir = Resolve-RepoRelativePath -Path $OutputDir

if (-not (Test-Path -LiteralPath $OutputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

if ($GenerateResponseFile.IsPresent) {
    $ResponseOutputPath = Resolve-RepoRelativePath -Path $ResponseOutputPath
    & $responseGeneratorScript -NonInteractive -OutputPath $ResponseOutputPath
    $ResponseFilePath = $ResponseOutputPath
}

$ResponseFilePath = Resolve-RepoRelativePath -Path $ResponseFilePath

if ([string]::IsNullOrWhiteSpace($ResponseFilePath)) {
    throw 'Response file path is required. Provide -ResponseFilePath or use -GenerateResponseFile.'
}

if (-not (Test-Path -LiteralPath $ResponseFilePath -PathType Leaf)) {
    throw "Response file not found at '$ResponseFilePath'."
}

if (-not (Test-Path -LiteralPath $BundlePath -PathType Leaf)) {
    throw "Bundle not found at '$BundlePath'. Build installer-bundle first."
}

$responseRaw = Get-Content -LiteralPath $ResponseFilePath -Raw
$response = $responseRaw | ConvertFrom-Json

$bundleLogPath = Join-Path $OutputDir 'bundle-install.log'
$summaryJsonPath = Join-Path $OutputDir 'bundle-run-summary.json'
$summaryMarkdownPath = Join-Path $OutputDir 'bundle-run-summary.md'

$layoutDir = $null
if ($LayoutOnly.IsPresent) {
    $layoutDir = Join-Path $OutputDir 'bundle-layout'
    New-Item -ItemType Directory -Path $layoutDir -Force | Out-Null
    $bundleArgs = @(
        '/layout', "`"$layoutDir`"",
        '/quiet',
        "/log `"$bundleLogPath`""
    )
}
else {
    $bundleArgs = @(
        '/quiet',
        '/norestart',
        "ADAGIO_RESPONSE_FILE_PATH=`"$ResponseFilePath`""
    )
}

$commandPreview = ('"{0}" {1}' -f $BundlePath, ($bundleArgs -join ' '))
$responseHash = Get-TextHash -Value $responseRaw

$summary = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    dryRun = [bool]$DryRun
    layoutOnly = [bool]$LayoutOnly
    bundlePath = $BundlePath
    bundleExists = $true
    responseFilePath = $ResponseFilePath
    responseFileHash = $responseHash
    responseSchemaVersion = if ($null -ne $response.schemaVersion) { [int]$response.schemaVersion } else { 0 }
    certificateMode = [string]$response.security.certificateMode
    apiKeyMode = [string]$response.security.apiKeyMode
    urls = [string]$response.network.urls
    allowedHosts = [string]$response.network.allowedHosts
    commandPreview = $commandPreview
    exitCode = $null
    success = $false
    outputDir = $OutputDir
    bundleLogPath = $bundleLogPath
}

if (-not $DryRun.IsPresent) {
    if ($LayoutOnly.IsPresent) {
        # Layout mode: run bundle to extract/layout without installing.
        # This exercises the Burn engine startup and BA pipe handshake
        # without performing an actual installation.
        $process = Start-Process -FilePath $BundlePath -ArgumentList $bundleArgs -Wait -PassThru -NoNewWindow
        $summary.exitCode = $process.ExitCode
        $summary.success = ($process.ExitCode -eq 0)
        # Clean up extracted layout files
        if (Test-Path -LiteralPath $layoutDir -PathType Container) {
            Remove-Item -LiteralPath $layoutDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        $argumentsWithLog = @($bundleArgs + "/log `"$bundleLogPath`"")
        $process = Start-Process -FilePath $BundlePath -ArgumentList $argumentsWithLog -Wait -PassThru -NoNewWindow
        $summary.exitCode = $process.ExitCode
        $summary.success = ($process.ExitCode -eq 0)
    }
}

if ($DryRun.IsPresent) {
    $summary.success = $true
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8

$markdownLines = @(
    '# Bundle Run Summary',
    '',
    "- GeneratedAtUtc: $($summary.generatedAtUtc)",
    "- DryRun: $($summary.dryRun)",
    "- LayoutOnly: $($summary.layoutOnly)",
    "- Success: $($summary.success)",
    "- ExitCode: $($summary.exitCode)",
    "- BundlePath: $($summary.bundlePath)",
    "- ResponseFilePath: $($summary.responseFilePath)",
    "- ResponseFileHash: $($summary.responseFileHash)",
    "- ResponseSchemaVersion: $($summary.responseSchemaVersion)",
    "- CertificateMode: $($summary.certificateMode)",
    "- ApiKeyMode: $($summary.apiKeyMode)",
    "- Urls: $($summary.urls)",
    "- AllowedHosts: $($summary.allowedHosts)",
    "- CommandPreview: $($summary.commandPreview)",
    "- BundleLogPath: $($summary.bundleLogPath)"
)

$markdownLines -join "`r`n" | Set-Content -LiteralPath $summaryMarkdownPath -Encoding UTF8

Write-Host "Bundle run summary JSON: $summaryJsonPath"
Write-Host "Bundle run summary Markdown: $summaryMarkdownPath"

if (-not $summary.success) {
    throw "Bundle install failed with exit code $($summary.exitCode). See '$bundleLogPath'."
}

if ($PassThru.IsPresent) {
    [pscustomobject]$summary
}
