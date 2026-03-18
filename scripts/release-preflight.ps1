param(
    [switch]$Ci,
    [switch]$SkipTagChecks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AgentVersion {
    [xml]$project = Get-Content (Join-Path $PSScriptRoot '..\machine-agent\AdagioMachineAgent.csproj')
    $versionNode = $project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
    if (-not $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.Version)) {
        throw 'Could not resolve machine-agent <Version> from machine-agent/AdagioMachineAgent.csproj.'
    }

    return $versionNode.Version.Trim()
}

function Get-ExtensionVersion {
    $packagePath = Join-Path $PSScriptRoot '..\controller-extension\package.json'
    $packageJson = Get-Content $packagePath -Raw | ConvertFrom-Json
    if (-not $packageJson.version) {
        throw 'Could not resolve extension version from controller-extension/package.json.'
    }

    return [string]$packageJson.version
}

function Assert-FileExists([string]$path, [string]$label) {
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "$label is required but was not found at $path"
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    $agentVersion = Get-AgentVersion
    $extensionVersion = Get-ExtensionVersion

    if ($agentVersion -ne $extensionVersion) {
        throw "Version mismatch: machine-agent=$agentVersion extension=$extensionVersion"
    }

    Write-Host "Version check passed: machine-agent and extension are both $agentVersion"

    $expectedInstallerVersion = "$agentVersion.0"
    $versionOutput = dotnet msbuild installer\AdagioMachineAgent.Setup.wixproj -target:PrintVersionInfo
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to evaluate installer version via PrintVersionInfo target.'
    }

    $actualInstallerVersion = ($versionOutput | Select-String 'AgentWixVersion=' | Select-Object -First 1).ToString().Split('=')[-1].Trim()
    if ([string]::IsNullOrWhiteSpace($actualInstallerVersion)) {
        throw 'Could not parse AgentWixVersion from installer PrintVersionInfo output.'
    }

    if ($actualInstallerVersion -ne $expectedInstallerVersion) {
        throw "Installer version mismatch: expected $expectedInstallerVersion actual $actualInstallerVersion"
    }

    Write-Host "Installer version check passed: $actualInstallerVersion"

    Assert-FileExists (Join-Path $repoRoot 'SECURITY.md') 'SECURITY.md'
    Assert-FileExists (Join-Path $repoRoot 'SUPPORT.md') 'SUPPORT.md'
    Assert-FileExists (Join-Path $repoRoot 'CHANGELOG.md') 'CHANGELOG.md'
    Write-Host 'Governance docs presence check passed.'

    Assert-FileExists (Join-Path $repoRoot 'docs\OBSERVABILITY_FIELDS.md') 'docs/OBSERVABILITY_FIELDS.md'
    Assert-FileExists (Join-Path $repoRoot 'docs\PILOT_RUNBOOK.md') 'docs/PILOT_RUNBOOK.md'
    Assert-FileExists (Join-Path $repoRoot 'docs\DIAGNOSTICS_TROUBLESHOOTING.md') 'docs/DIAGNOSTICS_TROUBLESHOOTING.md'
    Write-Host 'Observability docs presence check passed.'

    if (-not $SkipTagChecks) {
        $isTaggedBuild = ($env:APPVEYOR_REPO_TAG -eq 'true') -or ($env:GITHUB_REF_TYPE -eq 'tag')
        $tagName = if ($env:APPVEYOR_REPO_TAG_NAME) { $env:APPVEYOR_REPO_TAG_NAME } elseif ($env:GITHUB_REF_NAME) { $env:GITHUB_REF_NAME } else { '' }

        if ($isTaggedBuild -and -not [string]::IsNullOrWhiteSpace($tagName)) {
            if ($tagName -notmatch '^v(\d+\.\d+\.\d+)$') {
                throw "Release tag '$tagName' must follow format v<semver>, e.g. v0.2.0"
            }

            $tagVersion = $Matches[1]
            if ($tagVersion -ne $agentVersion) {
                throw "Release tag version mismatch: tag=$tagVersion machine-agent=$agentVersion"
            }

            $changelogPath = Join-Path $repoRoot 'CHANGELOG.md'
            $changelog = Get-Content $changelogPath -Raw
            if ($changelog -notmatch "## \[$([regex]::Escape($tagVersion))\]") {
                throw "CHANGELOG.md must contain a section header for version $tagVersion when building tag $tagName"
            }

            Write-Host "Tag and changelog checks passed for $tagName"
        }
    }

    Write-Host 'Release preflight checks completed successfully.'
}
finally {
    Pop-Location
}
