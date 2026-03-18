$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$generateDryRunScript = Join-Path $repoRoot 'scripts\generate-release-ops-dry-run.ps1'
$validateDryRunScript = Join-Path $repoRoot 'scripts\validate-release-ops-dry-run.ps1'

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
}
