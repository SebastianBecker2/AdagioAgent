param(
    [switch]$InstallDeps,
    [switch]$Compile,
    [switch]$PackageVsix,
    [switch]$RunTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# In PowerShell 7+, native stderr can be promoted to errors when
# ErrorActionPreference is Stop. npm emits benign deprecation warnings to stderr,
# so rely on native exit codes for pass/fail instead.
$previousNativeErrorPreference = $null
$hasNativeErrorPreference = $false
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $hasNativeErrorPreference = $true
    $previousNativeErrorPreference = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
}

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

function Invoke-NativeStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Command,
        [string[]]$Arguments = @()
    )

    Write-Host "==> $Name"
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # Native tools (npm/node) may write warnings to stderr. In Windows PowerShell,
        # stderr can become non-terminating errors when ErrorActionPreference=Stop.
        # We intentionally evaluate pass/fail by exit code for native commands.
        $ErrorActionPreference = "Continue"
        & $Command @Arguments
        $nativeExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($nativeExitCode -ne 0) {
        throw "$Name failed with exit code $nativeExitCode."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$extensionRoot = Join-Path $repoRoot "controller-extension"

Push-Location $extensionRoot
try {
    Invoke-NativeStep -Name "node --version" -Command "node" -Arguments @("--version")
    Invoke-NativeStep -Name "npm --version" -Command "npm" -Arguments @("--version")

    if ($InstallDeps) {
        Invoke-NativeStep -Name "npm ci" -Command "npm" -Arguments @("ci", "--loglevel=error")
        Invoke-NativeStep -Name "npm i --no-save @rolldown/binding-win32-x64-msvc" -Command "npm" -Arguments @("i", "--no-save", "--loglevel=error", "@rolldown/binding-win32-x64-msvc")
    }

    if ($Compile) {
        Invoke-NativeStep -Name "npm run compile" -Command "npm" -Arguments @("run", "compile")
    }

    if ($PackageVsix) {
        Invoke-NativeStep -Name "npm run package:vsix" -Command "npm" -Arguments @("run", "package:vsix")
    }

    if ($RunTests) {
        if (-not (Test-Path -LiteralPath ".\test-results" -PathType Container)) {
            New-Item -ItemType Directory -Path ".\test-results" | Out-Null
        }

        Invoke-NativeStep -Name "npm run test:ci" -Command "npm" -Arguments @("run", "test:ci")
    }
}
finally {
    if ($hasNativeErrorPreference) {
        $PSNativeCommandUseErrorActionPreference = $previousNativeErrorPreference
    }
    Pop-Location
}
