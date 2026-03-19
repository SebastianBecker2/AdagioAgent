param(
    [switch]$InstallDeps,
    [switch]$Compile,
    [switch]$PackageVsix,
    [switch]$RunTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host "==> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$extensionRoot = Join-Path $repoRoot "controller-extension"

Push-Location $extensionRoot
try {
    Invoke-Step -Name "node --version" -Action { node --version }
    Invoke-Step -Name "npm --version" -Action { npm --version }

    if ($InstallDeps) {
        Invoke-Step -Name "npm ci" -Action { npm ci }
        Invoke-Step -Name "npm i --no-save @rolldown/binding-win32-x64-msvc" -Action {
            npm i --no-save @rolldown/binding-win32-x64-msvc
        }
    }

    if ($Compile) {
        Invoke-Step -Name "npm run compile" -Action { npm run compile }
    }

    if ($PackageVsix) {
        Invoke-Step -Name "npm run package:vsix" -Action { npm run package:vsix }
    }

    if ($RunTests) {
        if (-not (Test-Path -LiteralPath ".\test-results" -PathType Container)) {
            New-Item -ItemType Directory -Path ".\test-results" | Out-Null
        }

        Invoke-Step -Name "npm run test:ci" -Action { npm run test:ci }
    }
}
finally {
    Pop-Location
}
