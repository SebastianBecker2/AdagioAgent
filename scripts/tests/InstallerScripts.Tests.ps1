$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$installerMatrixScript = Join-Path $repoRoot 'scripts\test-installer-bootstrap-matrix.ps1'

Describe 'Installer validation matrix script' {
    BeforeEach {
        $script:testRoot = Join-Path $env:TEMP ("adagio-installer-script-tests-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:testRoot -Force | Out-Null
        $script:outputDir = Join-Path $script:testRoot 'artifacts'
    }

    AfterEach {
        Remove-Item -LiteralPath $script:testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'writes failure summary artifacts when the MSI path is missing' {
        $missingMsiPath = Join-Path $script:testRoot 'missing.msi'

        { & $installerMatrixScript -MsiPath $missingMsiPath -OutputDir $script:outputDir -FailOnScenarioFailure } | Should Throw

        $summaryJsonPath = Join-Path $script:outputDir 'installer-validation-summary.json'
        $summaryMarkdownPath = Join-Path $script:outputDir 'installer-validation-summary.md'

        (Test-Path -LiteralPath $summaryJsonPath -PathType Leaf) | Should Be $true
        (Test-Path -LiteralPath $summaryMarkdownPath -PathType Leaf) | Should Be $true

        $summary = Get-Content -LiteralPath $summaryJsonPath -Raw | ConvertFrom-Json
        $summary.success | Should Be $false
        @($summary.scenarios).Count | Should Be 1
        $summary.scenarios[0].name | Should Be 'FreshSilentInstall'
        $summary.scenarios[0].status | Should Be 'failed'
        $summary.scenarios[0].message | Should Match 'MSI not found'
    }

    It 'writes failure summary artifacts for unsupported scenarios before install begins' {
        { & $installerMatrixScript -ScenarioNames 'UnsupportedScenario' -OutputDir $script:outputDir -FailOnScenarioFailure } | Should Throw

        $summaryJsonPath = Join-Path $script:outputDir 'installer-validation-summary.json'
        (Test-Path -LiteralPath $summaryJsonPath -PathType Leaf) | Should Be $true

        $summary = Get-Content -LiteralPath $summaryJsonPath -Raw | ConvertFrom-Json
        $summary.success | Should Be $false
        @($summary.scenarios).Count | Should Be 1
        $summary.scenarios[0].name | Should Be 'UnsupportedScenario'
        $summary.scenarios[0].status | Should Be 'failed'
        $summary.scenarios[0].message | Should Match 'Unsupported scenario'
    }

    It 'requires a baseline MSI path for adjacent upgrade scenarios' {
        $targetMsiPath = Join-Path $script:testRoot 'target.msi'
        Set-Content -LiteralPath $targetMsiPath -Value 'fixture' -Encoding ASCII

        { & $installerMatrixScript -ScenarioNames 'AdjacentUpgrade' -MsiPath $targetMsiPath -OutputDir $script:outputDir -FailOnScenarioFailure } | Should Throw

        $summaryJsonPath = Join-Path $script:outputDir 'installer-validation-summary.json'
        (Test-Path -LiteralPath $summaryJsonPath -PathType Leaf) | Should Be $true

        $summary = Get-Content -LiteralPath $summaryJsonPath -Raw | ConvertFrom-Json
        $summary.success | Should Be $false
        @($summary.scenarios).Count | Should Be 1
        $summary.scenarios[0].name | Should Be 'AdjacentUpgrade'
        $summary.scenarios[0].status | Should Be 'failed'
        $summary.scenarios[0].message | Should Match 'AdjacentUpgrade requires -PreviousMsiPath'
    }

    It 'uses runtime-compatible hash formatting for diagnostic snapshots' {
        $scriptText = Get-Content -LiteralPath $installerMatrixScript -Raw

        $scriptText | Should Match '\[System\.BitConverter\]::ToString\('
        $scriptText | Should Not Match 'Convert\]::ToHexString\('
    }

    It 'uses a compiled TLS validation callback instead of a PowerShell scriptblock' {
        $scriptText = Get-Content -LiteralPath $installerMatrixScript -Raw

        $scriptText | Should Match 'Add-Type -TypeDefinition'
        $scriptText | Should Match 'RemoteCertificateValidationCallback'
        $scriptText | Should Not Match 'ServerCertificateValidationCallback\s*=\s*\{\s*\$true\s*\}'
    }
}