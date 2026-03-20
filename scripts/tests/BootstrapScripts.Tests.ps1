$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$bootstrapScript = Join-Path $repoRoot 'scripts\bootstrap-agent.ps1'

function New-TestAppSettings {
    param([string]$Path)

    $payload = @{
        SecurityOptions = @{
            ApiKey = 'CHANGE_ME'
            HttpsCertificatePath = 'CHANGE_ME'
            HttpsCertificatePassword = 'CHANGE_ME'
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
        $script:existingFailureContent = $null
        $script:hadFailureFile = Test-Path -LiteralPath $script:bootstrapFailurePath -PathType Leaf

        if ($script:hadFailureFile) {
            $script:existingFailureContent = Get-Content -LiteralPath $script:bootstrapFailurePath -Raw
        }
    }

    AfterEach {
        if ($script:hadFailureFile) {
            Set-Content -LiteralPath $script:bootstrapFailurePath -Value $script:existingFailureContent -Encoding UTF8
        }
        else {
            Remove-Item -LiteralPath $script:bootstrapFailurePath -Force -ErrorAction SilentlyContinue
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

        { & $bootstrapScript -CertificatePath $script:certPath -WriteToAppSettings -AppSettingsPath $script:missingAppSettingsPath -SuppressSecretOutput } | Should Throw

        (Test-Path -LiteralPath $script:bootstrapFailurePath -PathType Leaf) | Should Be $true

        $failure = Get-Content -LiteralPath $script:bootstrapFailurePath -Raw | ConvertFrom-Json
        $failure.errorCode | Should Be 'AA1002'
        $failure.appSettingsPath | Should Be $script:missingAppSettingsPath
    }
}
