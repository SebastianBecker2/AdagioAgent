param(
    [string]$ScriptsRoot = (Join-Path $PSScriptRoot '.'),
    [int]$TimeoutSeconds = 20,
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\release-ops-script-smoke-results.json'),
    [string[]]$IncludeScripts,
    [switch]$UseFixtures,
    [switch]$CheckpointEach,
    [string]$CheckpointPath = (Join-Path $PSScriptRoot '..\artifacts\release-ops-script-smoke-checkpoint.log')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($TimeoutSeconds -le 0) {
    throw "TimeoutSeconds must be > 0. Provided: $TimeoutSeconds"
}

if (-not (Test-Path -LiteralPath $ScriptsRoot -PathType Container)) {
    throw "ScriptsRoot not found: $ScriptsRoot"
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$scriptFiles = @(Get-ChildItem -LiteralPath $ScriptsRoot -File -Filter '*.ps1' |
    Where-Object { $_.Name -notin @('smoke-test-release-ops-scripts.ps1') } |
    Sort-Object Name)

if ($IncludeScripts -and $IncludeScripts.Count -gt 0) {
    $includeSet = @{}
    foreach ($name in $IncludeScripts) {
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $includeSet[$name.Trim()] = $true
        }
    }

    $scriptFiles = @($scriptFiles | Where-Object { $includeSet.ContainsKey($_.Name) })
}

if ($CheckpointEach) {
    $checkpointDir = Split-Path -Parent $CheckpointPath
    if (-not (Test-Path -LiteralPath $checkpointDir -PathType Container)) {
        New-Item -ItemType Directory -Path $checkpointDir -Force | Out-Null
    }

    "# Release-Ops Script Smoke Checkpoint" | Set-Content -LiteralPath $CheckpointPath -Encoding UTF8
}

$results = New-Object System.Collections.Generic.List[object]
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$fixtureRoot = Join-Path $repoRoot 'artifacts\release-ops-script-smoke-fixtures'
$transientRepoFiles = New-Object System.Collections.Generic.List[string]

if ($UseFixtures) {
    if (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
}

$originalTag = $env:APPVEYOR_REPO_TAG
$originalTagName = $env:APPVEYOR_REPO_TAG_NAME
$originalGithubRefType = $env:GITHUB_REF_TYPE
$originalGithubRefName = $env:GITHUB_REF_NAME

$env:APPVEYOR_REPO_TAG = 'false'
Remove-Item Env:APPVEYOR_REPO_TAG_NAME -ErrorAction SilentlyContinue
Remove-Item Env:GITHUB_REF_TYPE -ErrorAction SilentlyContinue
Remove-Item Env:GITHUB_REF_NAME -ErrorAction SilentlyContinue

function Set-FileContent {
    param(
        [string]$Path,
        [string]$Content
    )

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
}

foreach ($scriptFile in $scriptFiles) {
    $scriptArgs = @()

    if ($UseFixtures) {
        switch ($scriptFile.Name) {
            'bootstrap-agent.ps1' {
                $certPath = Join-Path $fixtureRoot 'tls\agent.pfx'
                Set-FileContent -Path $certPath -Content 'fixture-cert'
                $scriptArgs = @('-CertificatePath', $certPath)
            }
            'generate-evidence-index.ps1' {
                $outputDir = Join-Path $fixtureRoot 'generated-evidence-indexes'
                $signoffDir = Join-Path $fixtureRoot 'signoffs-evidence-index'
                if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
                    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
                }
                if (-not (Test-Path -LiteralPath $signoffDir -PathType Container)) {
                    New-Item -ItemType Directory -Path $signoffDir -Force | Out-Null
                }
                $scriptArgs = @('-Version', '0.1.0', '-DateUtc', '2099-01-01', '-OutputDirectory', $outputDir, '-SignoffDirectory', $signoffDir, '-Force')
            }
            'generate-release-ops-closure-package-manifest.ps1' {
                $fixtureVersion = "9.9.$(Get-Random -Minimum 900 -Maximum 999)"
                $fixtureDate = '20990101'
                $signoffRelative = "docs/release-ops/signoffs/v$fixtureVersion-$fixtureDate.md"
                $evidenceRelative = "docs/release-ops/evidence/indexes/v$fixtureVersion-$fixtureDate-evidence.md"
                $signoffPath = Join-Path $repoRoot $signoffRelative
                $evidencePath = Join-Path $repoRoot $evidenceRelative

                Set-FileContent -Path $evidencePath -Content '# fixture evidence index'
                Set-FileContent -Path $signoffPath -Content ("# fixture signoff`n`n- Evidence index path: $evidenceRelative")

                $transientRepoFiles.Add($signoffPath) | Out-Null
                $transientRepoFiles.Add($evidencePath) | Out-Null

                $readinessRoot = Join-Path $fixtureRoot 'closure-manifest-readiness'
                if (-not (Test-Path -LiteralPath $readinessRoot -PathType Container)) {
                    New-Item -ItemType Directory -Path $readinessRoot -Force | Out-Null
                }

                Set-FileContent -Path (Join-Path $readinessRoot 'release-ops-tag-readiness-summary.json') -Content '{}'
                Set-FileContent -Path (Join-Path $readinessRoot 'release-ops-tag-readiness-history-index.json') -Content '{}'
                Set-FileContent -Path (Join-Path $readinessRoot 'release-ops-promotion-gate-report.json') -Content '{}'
                Set-FileContent -Path (Join-Path $readinessRoot 'release-ops-promotion-gate-trend-index.json') -Content '{}'

                $scriptArgs = @('-ReadinessRoot', $readinessRoot, '-OutputDir', $readinessRoot, '-TagName', "v$fixtureVersion")
            }
            'generate-release-ops-dry-run.ps1' {
                $outputRoot = Join-Path $fixtureRoot 'dry-run-output'
                $scriptArgs = @('-Version', '0.1.0', '-DateUtc', '2099-01-01', '-OutputRoot', $outputRoot, '-Force')
            }
            'generate-signoff-record.ps1' {
                $outputDir = Join-Path $fixtureRoot 'generated-signoffs'
                $scriptArgs = @('-Version', '0.1.0', '-DateUtc', '2099-01-01', '-OutputDirectory', $outputDir, '-Force')
            }
            'validate-support-bundle.ps1' {
                $bundleDir = Join-Path $fixtureRoot 'support-bundle'
                if (-not (Test-Path -LiteralPath $bundleDir -PathType Container)) {
                    New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null
                }

                Set-FileContent -Path (Join-Path $bundleDir 'machine-info.json') -Content '{}'
                Set-FileContent -Path (Join-Path $bundleDir 'service-status.json') -Content '{}'
                Set-FileContent -Path (Join-Path $bundleDir 'application-events.json') -Content '[]'

                $manifest = @{
                    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                    BundleDirectory = $bundleDir
                    ApiBaseUrl = 'https://localhost:5001'
                    OfflineMode = $false
                    ArtifactSchema = @{
                        RequiredArtifacts = @('machine-info.json', 'service-status.json', 'application-events.json')
                        OptionalArtifacts = @()
                    }
                    IncludedFiles = @('machine-info.json', 'service-status.json', 'application-events.json')
                    Notes = @('fixture')
                } | ConvertTo-Json -Depth 6

                Set-FileContent -Path (Join-Path $bundleDir 'manifest.json') -Content $manifest
                $scriptArgs = @('-BundlePath', $bundleDir)
            }
        }
    }

    if ($CheckpointEach) {
        Add-Content -LiteralPath $CheckpointPath -Value ("START {0} {1}" -f ([DateTimeOffset]::UtcNow.ToString('o')), $scriptFile.Name)
    }

    $argumentList = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $scriptFile.FullName) + $scriptArgs

    $process = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList $argumentList `
        -PassThru -WindowStyle Hidden

    $null = Wait-Process -Id $process.Id -Timeout $TimeoutSeconds -ErrorAction SilentlyContinue
    $process.Refresh()
    $timedOut = -not $process.HasExited

    if ($timedOut) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue

        if ($CheckpointEach) {
            Add-Content -LiteralPath $CheckpointPath -Value ("DONE  {0} {1} timeout" -f ([DateTimeOffset]::UtcNow.ToString('o')), $scriptFile.Name)
        }

        $results.Add([pscustomobject]@{
            script = $scriptFile.Name
            status = 'timeout'
            exitCode = $null
        }) | Out-Null

        continue
    }

    $status = if ($process.ExitCode -eq 0) { 'ok' } else { 'error' }

    if ($CheckpointEach) {
        Add-Content -LiteralPath $CheckpointPath -Value ("DONE  {0} {1} {2}" -f ([DateTimeOffset]::UtcNow.ToString('o')), $scriptFile.Name, $status)
    }

    $results.Add([pscustomobject]@{
        script = $scriptFile.Name
        status = $status
        exitCode = $process.ExitCode
    }) | Out-Null
}

if ($null -eq $originalTag) {
    Remove-Item Env:APPVEYOR_REPO_TAG -ErrorAction SilentlyContinue
}
else {
    $env:APPVEYOR_REPO_TAG = $originalTag
}

if ($null -eq $originalTagName) {
    Remove-Item Env:APPVEYOR_REPO_TAG_NAME -ErrorAction SilentlyContinue
}
else {
    $env:APPVEYOR_REPO_TAG_NAME = $originalTagName
}

if ($null -eq $originalGithubRefType) {
    Remove-Item Env:GITHUB_REF_TYPE -ErrorAction SilentlyContinue
}
else {
    $env:GITHUB_REF_TYPE = $originalGithubRefType
}

if ($null -eq $originalGithubRefName) {
    Remove-Item Env:GITHUB_REF_NAME -ErrorAction SilentlyContinue
}
else {
    $env:GITHUB_REF_NAME = $originalGithubRefName
}

if ($UseFixtures) {
    foreach ($path in $transientRepoFiles) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    if (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

$summary = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    scriptsRoot = $ScriptsRoot
    timeoutSeconds = $TimeoutSeconds
    total = $results.Count
    ok = @($results | Where-Object { $_.status -eq 'ok' }).Count
    error = @($results | Where-Object { $_.status -eq 'error' }).Count
    timeout = @($results | Where-Object { $_.status -eq 'timeout' }).Count
    results = $results.ToArray()
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

Write-Host "Release-ops script smoke test completed: $OutputPath"
Write-Host "Summary: total=$($summary.total), ok=$($summary.ok), error=$($summary.error), timeout=$($summary.timeout)"
if ($CheckpointEach) {
    Write-Host "Checkpoint log written: $CheckpointPath"
}
