$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$generateDryRunScript = Join-Path $repoRoot 'scripts\generate-release-ops-dry-run.ps1'
$validateDryRunScript = Join-Path $repoRoot 'scripts\validate-release-ops-dry-run.ps1'
$pruneDiagnosticsScript = Join-Path $repoRoot 'scripts\prune-release-ops-dryrun-diagnostics.ps1'
$updateDiagnosticsIndexScript = Join-Path $repoRoot 'scripts\update-release-ops-diagnostics-index.ps1'

function Get-LatestDryRunPackage {
    param([string]$Root)

    return Get-ChildItem -LiteralPath $Root -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

Describe 'Release-ops dry-run scripts' {
    BeforeEach {
        $script:testRoot = Join-Path $repoRoot ("artifacts\release-ops-dryrun-tests\" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:testRoot -Force | Out-Null
        $script:testVersion = "8.8.$(Get-Random -Minimum 100 -Maximum 999)"
    }

    AfterEach {
        if (Test-Path -LiteralPath $script:testRoot -PathType Container) {
            Remove-Item -LiteralPath $script:testRoot -Recurse -Force
        }
    }

    It 'generate-release-ops-dry-run creates manifest and required directories' {
        & $generateDryRunScript -OutputRoot $script:testRoot -Version $script:testVersion -Force

        $package = Get-LatestDryRunPackage -Root $script:testRoot
        $package | Should Not BeNullOrEmpty

        $manifest = Join-Path $package.FullName 'manifest.json'
        (Test-Path -LiteralPath $manifest -PathType Leaf) | Should Be $true

        (Test-Path -LiteralPath (Join-Path $package.FullName 'signoffs') -PathType Container) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $package.FullName 'evidence\indexes') -PathType Container) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $package.FullName 'evidence\support-bundles') -PathType Container) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $package.FullName 'evidence\correlation-traces') -PathType Container) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $package.FullName 'evidence\rollback') -PathType Container) | Should Be $true
        (Test-Path -LiteralPath (Join-Path $package.FullName 'evidence\upgrade-validation') -PathType Container) | Should Be $true
    }

    It 'validate-release-ops-dry-run passes on generated package' {
        & $generateDryRunScript -OutputRoot $script:testRoot -Version $script:testVersion -Force

        $package = Get-LatestDryRunPackage -Root $script:testRoot
        { & $validateDryRunScript -PackagePath $package.FullName } | Should Not Throw
    }

    It 'validate-release-ops-dry-run fails when a required fixture file is missing' {
        & $generateDryRunScript -OutputRoot $script:testRoot -Version $script:testVersion -Force

        $package = Get-LatestDryRunPackage -Root $script:testRoot
        $missingFixture = Join-Path $package.FullName "evidence\rollback\v$script:testVersion-dryrun-rollback.md"
        Remove-Item -LiteralPath $missingFixture -Force

        { & $validateDryRunScript -PackagePath $package.FullName } | Should Throw
    }

    It 'validate-release-ops-dry-run writes summary output with categorized issues on failure' {
        & $generateDryRunScript -OutputRoot $script:testRoot -Version $script:testVersion -Force

        $package = Get-LatestDryRunPackage -Root $script:testRoot
        $summaryPath = Join-Path $script:testRoot 'dryrun-summary.json'
        $missingFixture = Join-Path $package.FullName "evidence\\rollback\\v$script:testVersion-dryrun-rollback.md"
        Remove-Item -LiteralPath $missingFixture -Force

        { & $validateDryRunScript -PackagePath $package.FullName -SummaryOutputPath $summaryPath } | Should Throw
        (Test-Path -LiteralPath $summaryPath -PathType Leaf) | Should Be $true

        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $summary.success | Should Be $false
        ($summary.issues | Where-Object { $_.category -eq 'fixture' } | Measure-Object).Count | Should BeGreaterThan 0
    }

    It 'prune-release-ops-dryrun-diagnostics removes stale summaries and keeps recent files' {
        $diagnosticsRoot = Join-Path $script:testRoot 'diagnostics'
        New-Item -ItemType Directory -Path $diagnosticsRoot -Force | Out-Null

        $staleFile = Join-Path $diagnosticsRoot 'stale.json'
        $recentFile = Join-Path $diagnosticsRoot 'recent.json'
        Set-Content -LiteralPath $staleFile -Value '{}' -Encoding UTF8
        Set-Content -LiteralPath $recentFile -Value '{}' -Encoding UTF8

        (Get-Item -LiteralPath $staleFile).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-20)
        (Get-Item -LiteralPath $recentFile).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-1)

        { & $pruneDiagnosticsScript -DiagnosticsRoot $diagnosticsRoot -RetentionDays 14 } | Should Not Throw

        (Test-Path -LiteralPath $staleFile -PathType Leaf) | Should Be $false
        (Test-Path -LiteralPath $recentFile -PathType Leaf) | Should Be $true
    }

    It 'update-release-ops-diagnostics-index summarizes recent success and failure entries' {
        $diagnosticsRoot = Join-Path $script:testRoot 'diagnostics'
        New-Item -ItemType Directory -Path $diagnosticsRoot -Force | Out-Null

        $successSummary = Join-Path $diagnosticsRoot 'dryrun-validation-summary-success-a.json'
        $failureSummary = Join-Path $diagnosticsRoot 'dryrun-validation-summary-failure-b.json'

        @{
            generatedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-10).ToString('u')
            success = $true
            error = ''
            issues = @()
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $successSummary -Encoding UTF8

        @{
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
            success = $false
            error = 'fixture missing'
            issues = @(
                @{
                    category = 'fixture'
                    message = 'missing file'
                }
            )
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $failureSummary -Encoding UTF8

        { & $updateDiagnosticsIndexScript -DiagnosticsRoot $diagnosticsRoot -MaxEntries 20 } | Should Not Throw

        $indexJsonPath = Join-Path $diagnosticsRoot 'dryrun-diagnostics-index.json'
        $indexMdPath = Join-Path $diagnosticsRoot 'dryrun-diagnostics-index.md'

        (Test-Path -LiteralPath $indexJsonPath -PathType Leaf) | Should Be $true
        (Test-Path -LiteralPath $indexMdPath -PathType Leaf) | Should Be $true

        $index = Get-Content -LiteralPath $indexJsonPath -Raw | ConvertFrom-Json
        $index.totalEntries | Should Be 2
        $index.successCount | Should Be 1
        $index.failureCount | Should Be 1
    }
}
