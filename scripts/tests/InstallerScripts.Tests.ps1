$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$installerMatrixScript = Join-Path $repoRoot 'scripts\test-installer-bootstrap-matrix.ps1'
$responseGeneratorScript = Join-Path $repoRoot 'scripts\generate-installer-response-file.ps1'
$bundleRunnerScript = Join-Path $repoRoot 'scripts\run-installer-bundle-with-response.ps1'

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

Describe 'Installer response-file generator script' {
    BeforeEach {
        $script:testRoot = Join-Path $env:TEMP ("adagio-response-script-tests-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:testRoot -Force | Out-Null
        $script:responsePath = Join-Path $script:testRoot 'installer-response.json'
    }

    AfterEach {
        Remove-Item -LiteralPath $script:testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'writes a valid response file in non-interactive mode' {
        {
            & $responseGeneratorScript -NonInteractive -OutputPath $script:responsePath
        } | Should Not Throw

        (Test-Path -LiteralPath $script:responsePath -PathType Leaf) | Should Be $true

        $payload = Get-Content -LiteralPath $script:responsePath -Raw | ConvertFrom-Json
        $payload.schemaVersion | Should Be 1
        $payload.security.certificateMode | Should Be 'GeneratedCa'
        $payload.security.apiKeyMode | Should Be 'Generate'
        [string]::IsNullOrWhiteSpace([string]$payload.network.urls) | Should Be $false
        [string]::IsNullOrWhiteSpace([string]$payload.network.allowedHosts) | Should Be $false
        @($payload.agentOptions.allowedExecutablePaths).Count | Should BeGreaterThan 0
        @($payload.agentOptions.allowedWritablePaths).Count | Should BeGreaterThan 0
        @($payload.agentOptions.allowedReadablePaths).Count | Should BeGreaterThan 0
    }

    It 'rejects provided certificate mode without path and password' {
        {
            & $responseGeneratorScript -NonInteractive -OutputPath $script:responsePath -CertificateMode Provided
        } | Should Throw
    }

    It 'rejects provided API key mode without providedApiKey' {
        {
            & $responseGeneratorScript -NonInteractive -OutputPath $script:responsePath -ApiKeyMode Provided
        } | Should Throw
    }
}

Describe 'Installer bundle runner script' {
    BeforeEach {
        $script:testRoot = Join-Path $env:TEMP ("adagio-bundle-runner-tests-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:testRoot -Force | Out-Null

        $script:outputDir = Join-Path $script:testRoot 'artifacts'
        New-Item -ItemType Directory -Path $script:outputDir -Force | Out-Null

        $script:responsePath = Join-Path $script:testRoot 'response.json'
        & $responseGeneratorScript -NonInteractive -OutputPath $script:responsePath | Out-Null

        $script:bundlePath = Join-Path $script:testRoot 'AdagioMachineAgent.Bundle.exe'
        Set-Content -LiteralPath $script:bundlePath -Value 'fixture-bundle' -Encoding ASCII
    }

    AfterEach {
        Remove-Item -LiteralPath $script:testRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'writes dry-run JSON and Markdown summaries' {
        {
            & $bundleRunnerScript -BundlePath $script:bundlePath -ResponseFilePath $script:responsePath -OutputDir $script:outputDir -DryRun
        } | Should Not Throw

        $summaryJsonPath = Join-Path $script:outputDir 'bundle-run-summary.json'
        $summaryMarkdownPath = Join-Path $script:outputDir 'bundle-run-summary.md'

        (Test-Path -LiteralPath $summaryJsonPath -PathType Leaf) | Should Be $true
        (Test-Path -LiteralPath $summaryMarkdownPath -PathType Leaf) | Should Be $true

        $summary = Get-Content -LiteralPath $summaryJsonPath -Raw | ConvertFrom-Json
        $summary.dryRun | Should Be $true
        $summary.success | Should Be $true
        $summary.responseSchemaVersion | Should Be 1
        $summary.bundlePath | Should Be $script:bundlePath
        $summary.responseFilePath | Should Be $script:responsePath
    }

    It 'fails when response file path is missing' {
        {
            & $bundleRunnerScript -BundlePath $script:bundlePath -ResponseFilePath (Join-Path $script:testRoot 'missing-response.json') -OutputDir $script:outputDir -DryRun
        } | Should Throw
    }

    It 'fails when bundle path is missing' {
        {
            & $bundleRunnerScript -BundlePath (Join-Path $script:testRoot 'missing-bundle.exe') -ResponseFilePath $script:responsePath -OutputDir $script:outputDir -DryRun
        } | Should Throw
    }
}