$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$bootstrapScript = Join-Path $repoRoot 'scripts\bootstrap-agent.ps1'

function New-TestAppSettings {
    param(
        [string]$Path,
        [string]$ApiKey = 'CHANGE_ME',
        [string]$HttpsCertificatePath = 'CHANGE_ME',
        [string]$HttpsCertificatePassword = 'CHANGE_ME'
    )

    $payload = @{
        AllowedHosts = 'localhost;127.0.0.1'
        Urls = 'https://127.0.0.1:5443'
        SecurityOptions = @{
            RequireHttps = $true
            RequireApiKey = $true
            ApiKey = $ApiKey
            HttpsCertificatePath = $HttpsCertificatePath
            HttpsCertificatePassword = $HttpsCertificatePassword
        }
        AgentOptions = @{
            AllowedExecutablePaths = @('C:\Apps')
            AllowedWritablePaths = @('C:\Apps')
            AllowedReadablePaths = @('C:\Apps')
        }
    } | ConvertTo-Json -Depth 6

    Set-Content -LiteralPath $Path -Value $payload -Encoding UTF8
}

function Convert-IdentityToSid {
    param([object]$IdentityReference)

    if ($IdentityReference -is [System.Security.Principal.SecurityIdentifier]) {
        return $IdentityReference.Value
    }

    try {
        return $IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
    }
    catch {
        return $IdentityReference.Value
    }
}

Describe 'Bootstrap provisioning script' {
    BeforeEach {
        $script:testRoot = Join-Path $env:TEMP ("adagio-bootstrap-tests-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:testRoot -Force | Out-Null
        $script:isAdministrator = ([System.Security.Principal.WindowsPrincipal] [System.Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

        $script:certPath = Join-Path $script:testRoot 'agent.pfx'
        $script:appSettingsPath = Join-Path $script:testRoot 'appsettings.json'
        $script:handoffPath = Join-Path $script:testRoot 'bootstrap-secrets.json'
        $script:missingAppSettingsPath = Join-Path $script:testRoot 'missing-appsettings.json'

        # Use an existing file to avoid certificate enrollment dependencies in tests.
        Set-Content -LiteralPath $script:certPath -Value 'fixture' -Encoding UTF8
        New-TestAppSettings -Path $script:appSettingsPath

        $diagnosticsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'AdagioMachineAgent'
        $script:bootstrapFailurePath = Join-Path $diagnosticsRoot 'bootstrap-failure.json'
        $script:bootstrapFailureFallbackPath = Join-Path $env:TEMP 'AdagioMachineAgent-bootstrap-failure.json'
        $script:existingFailureContent = $null
        $script:hadFailureFile = Test-Path -LiteralPath $script:bootstrapFailurePath -PathType Leaf
        $script:existingFailureFallbackContent = $null
        $script:hadFailureFallbackFile = Test-Path -LiteralPath $script:bootstrapFailureFallbackPath -PathType Leaf

        if ($script:hadFailureFile) {
            $script:existingFailureContent = Get-Content -LiteralPath $script:bootstrapFailurePath -Raw
        }

        if ($script:hadFailureFallbackFile) {
            $script:existingFailureFallbackContent = Get-Content -LiteralPath $script:bootstrapFailureFallbackPath -Raw
        }
    }

    AfterEach {
        if ($script:hadFailureFile) {
            Set-Content -LiteralPath $script:bootstrapFailurePath -Value $script:existingFailureContent -Encoding UTF8
        }
        else {
            Remove-Item -LiteralPath $script:bootstrapFailurePath -Force -ErrorAction SilentlyContinue
        }

        if ($script:hadFailureFallbackFile) {
            Set-Content -LiteralPath $script:bootstrapFailureFallbackPath -Value $script:existingFailureFallbackContent -Encoding UTF8
        }
        else {
            Remove-Item -LiteralPath $script:bootstrapFailureFallbackPath -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path -LiteralPath $script:testRoot -PathType Container) {
            try {
                Remove-Item -LiteralPath $script:testRoot -Recurse -Force -ErrorAction Stop
            }
            catch {
                # Non-admin runs cannot clean up handoff files once ACL is restricted to SYSTEM/Administrators.
            }
        }
    }

    It 'writes startup-critical values to appsettings' {
        { & $bootstrapScript -CertificatePath $script:certPath -WriteToAppSettings -AppSettingsPath $script:appSettingsPath -SuppressSecretOutput } | Should Not Throw

        $appSettings = Get-Content -LiteralPath $script:appSettingsPath -Raw | ConvertFrom-Json
        $appSettings.SecurityOptions.ApiKey | Should Not Be 'CHANGE_ME'
        $appSettings.SecurityOptions.HttpsCertificatePath | Should Be $script:certPath
        $appSettings.SecurityOptions.HttpsCertificatePassword | Should Not Be 'CHANGE_ME'
    }

    It 'accepts explicit DNS and IP SAN parameters during bootstrap' {
        {
            $params = @{
                CertificatePath = $script:certPath
                DnsNames = @('adagio-vm', 'localhost')
                IpAddresses = @('127.0.0.1', '192.168.178.59')
                WriteToAppSettings = $true
                AppSettingsPath = $script:appSettingsPath
                SuppressSecretOutput = $true
            }

            & $bootstrapScript @params
        } | Should Not Throw

        $appSettings = Get-Content -LiteralPath $script:appSettingsPath -Raw | ConvertFrom-Json
        $appSettings.SecurityOptions.HttpsCertificatePath | Should Be $script:certPath
    }

    It 'preserves existing certificate password and API key when reusing existing certificate' {
        $existingApiKey = 'existing-key-value'
        $existingCertPassword = 'existing-cert-password'
        New-TestAppSettings -Path $script:appSettingsPath -ApiKey $existingApiKey -HttpsCertificatePath $script:certPath -HttpsCertificatePassword $existingCertPassword

        {
            & $bootstrapScript -CertificatePath $script:certPath -WriteToAppSettings -AppSettingsPath $script:appSettingsPath -SuppressSecretOutput
        } | Should Not Throw

        $appSettings = Get-Content -LiteralPath $script:appSettingsPath -Raw | ConvertFrom-Json
        $appSettings.SecurityOptions.ApiKey | Should Be $existingApiKey
        $appSettings.SecurityOptions.HttpsCertificatePassword | Should Be $existingCertPassword
        $appSettings.SecurityOptions.HttpsCertificatePath | Should Be $script:certPath
    }

    It 'accepts provided API key mode and writes explicit key to appsettings' {
        $existingCertPassword = 'existing-cert-password'
        New-TestAppSettings -Path $script:appSettingsPath -ApiKey 'CHANGE_ME' -HttpsCertificatePath $script:certPath -HttpsCertificatePassword $existingCertPassword

        {
            & $bootstrapScript -CertificatePath $script:certPath -ApiKeyMode Provided -ProvidedApiKey 'operator-provided-key' -WriteToAppSettings -AppSettingsPath $script:appSettingsPath -SuppressSecretOutput
        } | Should Not Throw

        $appSettings = Get-Content -LiteralPath $script:appSettingsPath -Raw | ConvertFrom-Json
        $appSettings.SecurityOptions.ApiKey | Should Be 'operator-provided-key'
        $appSettings.SecurityOptions.HttpsCertificatePassword | Should Be $existingCertPassword
    }

    It 'applies response-file values for silent parity when explicit CLI values are not provided' {
        $existingCertPassword = 'existing-cert-password'
        New-TestAppSettings -Path $script:appSettingsPath -ApiKey 'existing-key' -HttpsCertificatePath $script:certPath -HttpsCertificatePassword $existingCertPassword
        $responsePath = Join-Path $script:testRoot 'response.json'

        $responsePayload = @{
            security = @{
                apiKeyMode = 'Provided'
                providedApiKey = 'response-file-api-key'
                requireHttps = $false
                requireApiKey = $true
            }
            network = @{
                urls = 'https://10.0.0.2:5443'
                allowedHosts = '10.0.0.2;agent-host'
            }
            agentOptions = @{
                allowedExecutablePaths = @('C:\Tools')
                allowedWritablePaths = @('C:\Logs')
                allowedReadablePaths = @('C:\Logs', 'C:\Tools')
            }
        } | ConvertTo-Json -Depth 10

        Set-Content -LiteralPath $responsePath -Value $responsePayload -Encoding UTF8

        {
            & $bootstrapScript -CertificatePath $script:certPath -ResponseFilePath $responsePath -WriteToAppSettings -AppSettingsPath $script:appSettingsPath -SuppressSecretOutput
        } | Should Not Throw

        $appSettings = Get-Content -LiteralPath $script:appSettingsPath -Raw | ConvertFrom-Json
        $appSettings.SecurityOptions.ApiKey | Should Be 'response-file-api-key'
        $appSettings.SecurityOptions.HttpsCertificatePassword | Should Be $existingCertPassword
        $appSettings.SecurityOptions.RequireHttps | Should Be $false
        $appSettings.SecurityOptions.RequireApiKey | Should Be $true
        $appSettings.Urls | Should Be 'https://10.0.0.2:5443'
        $appSettings.AllowedHosts | Should Be '10.0.0.2;agent-host'
        @($appSettings.AgentOptions.AllowedExecutablePaths) | Should Be @('C:\Tools')
        @($appSettings.AgentOptions.AllowedWritablePaths) | Should Be @('C:\Logs')
        @($appSettings.AgentOptions.AllowedReadablePaths) | Should Be @('C:\Logs', 'C:\Tools')
    }

    It 'fails fast for unsupported response schemaVersion' {
        $responsePath = Join-Path $script:testRoot 'unsupported-response.json'
        $payload = @{
            schemaVersion = 2
            security = @{ apiKeyMode = 'Generate' }
        } | ConvertTo-Json -Depth 5
        Set-Content -LiteralPath $responsePath -Value $payload -Encoding UTF8

        {
            & $bootstrapScript -CertificatePath $script:certPath -ResponseFilePath $responsePath -SuppressSecretOutput
        } | Should Throw

        $failurePath = if (Test-Path -LiteralPath $script:bootstrapFailurePath -PathType Leaf) {
            $script:bootstrapFailurePath
        }
        elseif (Test-Path -LiteralPath $script:bootstrapFailureFallbackPath -PathType Leaf) {
            $script:bootstrapFailureFallbackPath
        }
        else {
            $null
        }

        ($null -ne $failurePath) | Should Be $true
        $failure = Get-Content -LiteralPath $failurePath -Raw | ConvertFrom-Json
        $failure.errorCode | Should Be 'AA1004'
    }

    It 'writes secret handoff with restricted ACL for SYSTEM and Administrators' -Skip:(-not $script:isAdministrator) {
        { & $bootstrapScript -CertificatePath $script:certPath -WriteSecretHandoff -SecretHandoffPath $script:handoffPath -SuppressSecretOutput } | Should Not Throw

        (Test-Path -LiteralPath $script:handoffPath -PathType Leaf) | Should Be $true

        $handoff = Get-Content -LiteralPath $script:handoffPath -Raw | ConvertFrom-Json
        [string]::IsNullOrWhiteSpace($handoff.apiKey) | Should Be $false
        [string]::IsNullOrWhiteSpace($handoff.httpsCertificatePassword) | Should Be $false
        $handoff.httpsCertificatePath | Should Be $script:certPath
        $handoff.httpsCaCertificatePemPath | Should Be (Join-Path $script:testRoot 'agent-ca.pem')
        $handoff.httpsCaCertificatePfxPath | Should Be (Join-Path $script:testRoot 'agent-ca.pfx')

        $acl = Get-Acl -LiteralPath $script:handoffPath
        $acl.AreAccessRulesProtected | Should Be $true

        $sidValues = @($acl.Access | ForEach-Object { Convert-IdentityToSid -IdentityReference $_.IdentityReference })
        ($sidValues -contains 'S-1-5-18') | Should Be $true
        ($sidValues -contains 'S-1-5-32-544') | Should Be $true
    }

    It 'emits AA1002 in bootstrap-failure artifact when appsettings path is missing' {
        Remove-Item -LiteralPath $script:bootstrapFailurePath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $script:bootstrapFailureFallbackPath -Force -ErrorAction SilentlyContinue

        { & $bootstrapScript -CertificatePath $script:certPath -WriteToAppSettings -AppSettingsPath $script:missingAppSettingsPath -SuppressSecretOutput } | Should Throw

        $failurePath = if (Test-Path -LiteralPath $script:bootstrapFailurePath -PathType Leaf) {
            $script:bootstrapFailurePath
        }
        elseif (Test-Path -LiteralPath $script:bootstrapFailureFallbackPath -PathType Leaf) {
            $script:bootstrapFailureFallbackPath
        }
        else {
            $null
        }

        ($null -ne $failurePath) | Should Be $true

        $failure = Get-Content -LiteralPath $failurePath -Raw | ConvertFrom-Json
        $failure.errorCode | Should Be 'AA1002'
        $failure.appSettingsPath | Should Be $script:missingAppSettingsPath
    }
}
