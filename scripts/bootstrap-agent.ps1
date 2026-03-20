param(
    [string]$CertificatePath = "C:\ProgramData\AdagioMachineAgent\tls\agent.pfx",
    [string]$CaCertificatePemPath = "",
    [string]$CaCertificatePfxPath = "",
    [bool]$UseCertificateAuthority = $true,
    [string[]]$DnsNames = @("localhost"),
    [string[]]$IpAddresses = @("127.0.0.1"),
    [switch]$PersistToEnvironment,
    [switch]$WriteToAppSettings,
    [string]$AppSettingsPath = "machine-agent\appsettings.json",
    [switch]$ForceRegenerate,
    [switch]$WriteSecretHandoff,
    [string]$SecretHandoffPath = "C:\ProgramData\AdagioMachineAgent\bootstrap-secrets.json",
    [switch]$SuppressSecretOutput,
    [string]$LogPath = "",
    [switch]$TrustCertificate,
    [switch]$StartService
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$bootstrapDiagnosticsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) "AdagioMachineAgent"
$bootstrapFailurePath = Join-Path $bootstrapDiagnosticsRoot "bootstrap-failure.json"

if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
    $logDir = Split-Path -Parent $LogPath
    if (-not [string]::IsNullOrWhiteSpace($logDir) -and -not (Test-Path -LiteralPath $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }

    Start-Transcript -LiteralPath $LogPath -Force | Out-Null
}

try {

if ($WriteToAppSettings.IsPresent -and -not [System.IO.Path]::IsPathRooted($AppSettingsPath)) {
    $coLocatedAppSettings = Join-Path -Path (Split-Path -Parent $PSCommandPath) -ChildPath "appsettings.json"
    if ((Test-Path -LiteralPath $coLocatedAppSettings -PathType Leaf) -and -not (Test-Path -LiteralPath $AppSettingsPath -PathType Leaf)) {
        $AppSettingsPath = $coLocatedAppSettings
    }
}

function New-RandomString {
    param(
        [int]$ByteLength = 32
    )

    $bytes = New-Object byte[] $ByteLength
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }
    return [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function Get-FailureMetadata {
    param(
        [string]$ErrorMessage
    )

    if ($ErrorMessage -match 'Access denied|0x80070005|0x80090010') {
        return @{
            errorCode = 'AA1001'
            suggestedAction = 'Re-run installer as administrator and ensure LocalMachine certificate store access is allowed.'
        }
    }

    if ($ErrorMessage -match 'appsettings file not found') {
        return @{
            errorCode = 'AA1002'
            suggestedAction = 'Verify appsettings.json exists in the installation folder before bootstrap runs.'
        }
    }

    if ($ErrorMessage -match 'Failed to create bootstrap certificate') {
        return @{
            errorCode = 'AA1003'
            suggestedAction = 'Ensure certificate services are available and check local security policy restrictions for certificate enrollment.'
        }
    }

    return @{
        errorCode = 'AA1099'
        suggestedAction = 'Inspect bootstrap.log for detailed command output, then retry installation.'
    }
}

function Protect-SecretHandoffFile {
    param(
        [string]$Path
    )

    $acl = New-Object System.Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)

    $rights = [System.Security.AccessControl.FileSystemRights]::FullControl
    $inheritance = [System.Security.AccessControl.InheritanceFlags]::None
    $propagation = [System.Security.AccessControl.PropagationFlags]::None
    $accessType = [System.Security.AccessControl.AccessControlType]::Allow

    $adminSid = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null)
    $systemSid = New-Object System.Security.Principal.SecurityIdentifier([System.Security.Principal.WellKnownSidType]::LocalSystemSid, $null)

    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule($adminSid, $rights, $inheritance, $propagation, $accessType)
    $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule($systemSid, $rights, $inheritance, $propagation, $accessType)
    $acl.AddAccessRule($adminRule)
    $acl.AddAccessRule($systemRule)

    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Get-CertificateSanTextExtension {
    param(
        [string[]]$DnsNames,
        [string[]]$IpAddresses
    )

    $sanEntries = @()

    foreach ($dnsName in @($DnsNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $sanEntries += "DNS=$dnsName"
    }

    foreach ($ipAddress in @($IpAddresses | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $sanEntries += "IPAddress=$ipAddress"
    }

    if ($sanEntries.Count -eq 0) {
        throw "At least one DNS name or IP address must be provided for the HTTPS certificate subject alternative name."
    }

    return "2.5.29.17={text}" + ($sanEntries -join "&")
}

function Get-DefaultBootstrapDnsNames {
    $dnsNames = @()

    if (-not [string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) {
        $dnsNames += $env:COMPUTERNAME
    }

    $dnsNames += "localhost"

    return @($dnsNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Get-DefaultBootstrapIpAddresses {
    $ipAddresses = @("127.0.0.1")

    try {
        $networkInterfaces = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()
        foreach ($networkInterface in $networkInterfaces) {
            if ($networkInterface.OperationalStatus -ne [System.Net.NetworkInformation.OperationalStatus]::Up) {
                continue
            }

            foreach ($unicastAddress in $networkInterface.GetIPProperties().UnicastAddresses) {
                $address = $unicastAddress.Address
                if ($null -eq $address) {
                    continue
                }

                if ($address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
                    continue
                }

                if ([System.Net.IPAddress]::IsLoopback($address)) {
                    continue
                }

                $addressText = $address.ToString()
                if ($addressText.StartsWith("169.254.", [System.StringComparison]::Ordinal)) {
                    continue
                }

                $ipAddresses += $addressText
            }
        }
    }
    catch {
        # Fall back to loopback-only if adapter discovery is unavailable.
    }

    return @($ipAddresses | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Export-CertificatePem {
    param(
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [string]$Path
    )

    $derBytes = $Certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    $base64 = [Convert]::ToBase64String($derBytes, [System.Base64FormattingOptions]::InsertLineBreaks)
    $pem = "-----BEGIN CERTIFICATE-----`r`n$base64`r`n-----END CERTIFICATE-----`r`n"
    Set-Content -LiteralPath $Path -Value $pem -Encoding Ascii
}

$certDirectory = Split-Path -Parent $CertificatePath
if (-not (Test-Path -LiteralPath $certDirectory)) {
    New-Item -ItemType Directory -Path $certDirectory -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($CaCertificatePemPath)) {
    $CaCertificatePemPath = Join-Path -Path $certDirectory -ChildPath "agent-ca.pem"
}

if ([string]::IsNullOrWhiteSpace($CaCertificatePfxPath)) {
    $CaCertificatePfxPath = Join-Path -Path $certDirectory -ChildPath "agent-ca.pfx"
}

if (-not $PSBoundParameters.ContainsKey('DnsNames')) {
    $DnsNames = Get-DefaultBootstrapDnsNames
}

if (-not $PSBoundParameters.ContainsKey('IpAddresses')) {
    $IpAddresses = Get-DefaultBootstrapIpAddresses
}

$certificatePassword = New-RandomString -ByteLength 24
$caCertificatePassword = New-RandomString -ByteLength 24
$apiKey = New-RandomString -ByteLength 32

$pfxPassword = ConvertTo-SecureString -String $certificatePassword -AsPlainText -Force
$caPfxPassword = ConvertTo-SecureString -String $caCertificatePassword -AsPlainText -Force
$createCertificate = $ForceRegenerate.IsPresent -or -not (Test-Path -LiteralPath $CertificatePath)

if ($createCertificate) {
    $subjectNames = @($DnsNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($subjectNames.Count -eq 0) {
        $subjectNames = @($IpAddresses | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($subjectNames.Count -eq 0) {
        throw "At least one DNS name or IP address must be provided when generating the HTTPS certificate."
    }

    $sanTextExtension = Get-CertificateSanTextExtension -DnsNames $DnsNames -IpAddresses $IpAddresses
    $subjectCommonName = ($subjectNames | Where-Object {
            -not $_.Equals("localhost", [System.StringComparison]::OrdinalIgnoreCase) -and
            -not $_.Equals("127.0.0.1", [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

    if ([string]::IsNullOrWhiteSpace($subjectCommonName)) {
        $subjectCommonName = $subjectNames[0]
    }

    $candidateStores = @(
        "Cert:\LocalMachine\My",
        "Cert:\CurrentUser\My"
    )

    $cert = $null
    $caCert = $null
    $selectedStore = $null
    $lastError = $null

    foreach ($store in $candidateStores) {
        try {
            if ($UseCertificateAuthority) {
                $caCertParams = @{
                    Subject           = "CN=AdagioMachineAgent Bootstrap Root CA"
                    CertStoreLocation = $store
                    FriendlyName      = "AdagioMachineAgent Bootstrap Root CA"
                    NotAfter          = (Get-Date).AddYears(10)
                    KeyExportPolicy   = "Exportable"
                    KeyUsageProperty  = "Sign"
                    KeyUsage          = "CertSign", "CRLSign", "DigitalSignature"
                    TextExtension     = @("2.5.29.19={critical}{text}CA=true&pathlength=1")
                }

                $caCert = New-SelfSignedCertificate @caCertParams

                $newCertParams = @{
                    Subject           = "CN=$subjectCommonName"
                    CertStoreLocation = $store
                    FriendlyName      = "AdagioMachineAgent Bootstrap"
                    NotAfter          = (Get-Date).AddYears(2)
                    KeyExportPolicy   = "Exportable"
                    KeyUsageProperty  = "All"
                    KeyUsage          = "DigitalSignature", "KeyEncipherment"
                    TextExtension     = @($sanTextExtension, "2.5.29.37={text}1.3.6.1.5.5.7.3.1")
                    Signer            = $caCert
                }
            }
            else {
                $newCertParams = @{
                    Subject           = "CN=$subjectCommonName"
                    CertStoreLocation = $store
                    FriendlyName      = "AdagioMachineAgent Bootstrap"
                    NotAfter          = (Get-Date).AddYears(2)
                    KeyExportPolicy   = "Exportable"
                    TextExtension     = @($sanTextExtension)
                }
            }

            $cert = New-SelfSignedCertificate @newCertParams
            $selectedStore = $store
            break
        }
        catch {
            $lastError = $_
        }
    }

    if (-not $cert -or [string]::IsNullOrWhiteSpace($selectedStore)) {
        throw "Failed to create bootstrap certificate in LocalMachine and CurrentUser stores. Last error: $($lastError.Exception.Message)"
    }

    $exportParams = @{
        Cert     = "$selectedStore\$($cert.Thumbprint)"
        FilePath = $CertificatePath
        Password = $pfxPassword
        Force    = $true
    }

    Export-PfxCertificate @exportParams | Out-Null

    if ($UseCertificateAuthority -and $null -ne $caCert) {
        $caExportParams = @{
            Cert     = "$selectedStore\$($caCert.Thumbprint)"
            FilePath = $CaCertificatePfxPath
            Password = $caPfxPassword
            Force    = $true
        }

        Export-PfxCertificate @caExportParams | Out-Null
        Export-CertificatePem -Certificate $caCert -Path $CaCertificatePemPath
    }
    else {
        Export-CertificatePem -Certificate $cert -Path $CaCertificatePemPath
    }
}

if ($PersistToEnvironment.IsPresent) {
    [Environment]::SetEnvironmentVariable("SecurityOptions__HttpsCertificatePath", $CertificatePath, "User")
    [Environment]::SetEnvironmentVariable("SecurityOptions__HttpsCertificatePassword", $certificatePassword, "User")
    [Environment]::SetEnvironmentVariable("SecurityOptions__ApiKey", $apiKey, "User")
}

if ($WriteToAppSettings.IsPresent) {
    if (-not (Test-Path -LiteralPath $AppSettingsPath)) {
        throw "appsettings file not found at '$AppSettingsPath'."
    }

    $config = Get-Content -LiteralPath $AppSettingsPath -Raw | ConvertFrom-Json
    $config.SecurityOptions.HttpsCertificatePath = $CertificatePath
    $config.SecurityOptions.HttpsCertificatePassword = $certificatePassword
    $config.SecurityOptions.ApiKey = $apiKey

    $json = $config | ConvertTo-Json -Depth 16
    Set-Content -LiteralPath $AppSettingsPath -Value $json -Encoding UTF8
}

if ($WriteSecretHandoff.IsPresent) {
    $secretHandoffDirectory = Split-Path -Parent $SecretHandoffPath
    if (-not [string]::IsNullOrWhiteSpace($secretHandoffDirectory) -and -not (Test-Path -LiteralPath $secretHandoffDirectory)) {
        New-Item -ItemType Directory -Path $secretHandoffDirectory -Force | Out-Null
    }

    $handoffPayload = [pscustomobject]@{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
        apiKey = $apiKey
        httpsCertificatePassword = $certificatePassword
        httpsCertificatePath = $CertificatePath
        httpsCaCertificatePemPath = $CaCertificatePemPath
        httpsCaCertificatePfxPath = $CaCertificatePfxPath
    }

    $handoffPayload | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $SecretHandoffPath -Encoding UTF8
    Protect-SecretHandoffFile -Path $SecretHandoffPath
}

Write-Host "Bootstrap completed."
Write-Host "Certificate path: $CertificatePath"
if (Test-Path -LiteralPath $CaCertificatePemPath) {
    Write-Host "PEM certificate path: $CaCertificatePemPath"
}
Write-Host "API key generated (hidden)."
Write-Host "Persisted to user environment: $($PersistToEnvironment.IsPresent)"
Write-Host "Written to appsettings: $($WriteToAppSettings.IsPresent)"
Write-Host "Secret handoff file written: $($WriteSecretHandoff.IsPresent)"
if ($WriteSecretHandoff.IsPresent) {
    Write-Host "Secret handoff path: $SecretHandoffPath"
}

if (-not $SuppressSecretOutput.IsPresent) {
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "1. Restart the AdagioMachineAgent service after updating config/environment."
    Write-Host "2. Configure adagioAgent.vmAgentApiKey in VS Code with the generated key value."
    Write-Host ""
    Write-Host "Generated values (copy securely now):"
    Write-Host "SecurityOptions__ApiKey=$apiKey"
    Write-Host "SecurityOptions__HttpsCertificatePassword=$certificatePassword"
}

if ($TrustCertificate.IsPresent) {
    if ($createCertificate -and (Test-Path -LiteralPath $CertificatePath)) {
        Write-Host "Importing certificate into LocalMachine\Root trust store..."
        try {
            if ($UseCertificateAuthority -and (Test-Path -LiteralPath $CaCertificatePfxPath)) {
                $importedCert = Import-PfxCertificate -FilePath $CaCertificatePfxPath -CertStoreLocation "Cert:\LocalMachine\Root" -Password $caPfxPassword
            }
            else {
                $importedCert = Import-PfxCertificate -FilePath $CertificatePath -CertStoreLocation "Cert:\LocalMachine\Root" -Password $pfxPassword
            }
            Write-Host "Certificate trusted (thumbprint: $($importedCert.Thumbprint))."
        }
        catch {
            Write-Warning "Could not add certificate to LocalMachine\Root: $($_.Exception.Message)"
        }
    }
    elseif (-not $createCertificate) {
        Write-Host "Skipping certificate trust: existing certificate retained. Import it manually using the password in appsettings.json."
    }
}

if ($StartService.IsPresent) {
    $svc = Get-Service -Name "AdagioMachineAgent" -ErrorAction SilentlyContinue
    if ($null -ne $svc) {
        Write-Host "Restarting AdagioMachineAgent service..."
        Restart-Service -Name "AdagioMachineAgent" -Force
        $svc = Get-Service -Name "AdagioMachineAgent"
        Write-Host "Service state: $($svc.Status)"
    }
    else {
        Write-Host "WARNING: AdagioMachineAgent service not found. Install the MSI first, or start the service manually."
    }
}
}
catch {
    try {
        New-Item -ItemType Directory -Path $bootstrapDiagnosticsRoot -Force | Out-Null
        $metadata = Get-FailureMetadata -ErrorMessage $_.Exception.Message

        $failure = @{
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
            error = $_.Exception.Message
            exceptionType = $_.Exception.GetType().FullName
            scriptPath = $PSCommandPath
            appSettingsPath = $AppSettingsPath
            certificatePath = $CertificatePath
            secretHandoffPath = $SecretHandoffPath
            logPath = $LogPath
            errorCode = $metadata.errorCode
            suggestedAction = $metadata.suggestedAction
        }

        $failure | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $bootstrapFailurePath -Encoding UTF8
    }
    catch {
        # Ignore diagnostics-write errors and preserve original exception context.
    }

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Write-Error "Bootstrap provisioning failed. See '$LogPath' and '$bootstrapFailurePath'. Error: $($_.Exception.Message)"
    }
    else {
        Write-Error "Bootstrap provisioning failed. See '$bootstrapFailurePath'. Error: $($_.Exception.Message)"
    }

    throw
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Stop-Transcript | Out-Null
    }
}
