param(
    [string]$CertificatePath = "C:\ProgramData\AdagioMachineAgent\tls\agent.pfx",
    [string]$CaCertificatePemPath = "",
    [string]$CaCertificatePfxPath = "",
    [bool]$UseCertificateAuthority = $true,
    [string[]]$DnsNames = @("localhost"),
    [string[]]$IpAddresses = @("127.0.0.1"),
    [string]$CertificateMode = "",
    [string]$ProvidedCertificatePath = "",
    [string]$ProvidedCertificatePassword = "",
    [string]$ApiKeyMode = "Generate",
    [string]$ProvidedApiKey = "",
    [string]$ResponseFilePath = "",
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
$bootstrapFailureFallbackPath = Join-Path $env:TEMP "AdagioMachineAgent-bootstrap-failure.json"
$failureOutputPath = $bootstrapFailurePath
$resolvedCertificateMode = 'Unknown'
$resolvedApiKeyMode = 'Unknown'

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

    if ($ErrorMessage -match 'Unsupported response schemaVersion') {
        return @{
            errorCode = 'AA1004'
            suggestedAction = 'Regenerate installer response file with scripts/generate-installer-response-file.ps1 or use a supported schemaVersion.'
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

function Resolve-CertificateMode {
    param(
        [string]$RequestedMode,
        [bool]$UseCertificateAuthority,
        [hashtable]$BoundParameters
    )

    if ([string]::IsNullOrWhiteSpace($RequestedMode)) {
        if ($BoundParameters.ContainsKey('UseCertificateAuthority') -and -not $UseCertificateAuthority) {
            return 'GeneratedLeaf'
        }

        return 'GeneratedCa'
    }

    $normalized = $RequestedMode.Trim().ToLowerInvariant()
    switch ($normalized) {
        'generatedca' { return 'GeneratedCa' }
        'generatedleaf' { return 'GeneratedLeaf' }
        'provided' { return 'Provided' }
        default {
            throw "Unsupported CertificateMode '$RequestedMode'. Supported values: GeneratedCa, GeneratedLeaf, Provided."
        }
    }
}

function Resolve-ApiKeyMode {
    param([string]$RequestedMode)

    $normalized = if ([string]::IsNullOrWhiteSpace($RequestedMode)) {
        'generate'
    }
    else {
        $RequestedMode.Trim().ToLowerInvariant()
    }

    switch ($normalized) {
        'generate' { return 'Generate' }
        'provided' { return 'Provided' }
        default {
            throw "Unsupported ApiKeyMode '$RequestedMode'. Supported values: Generate, Provided."
        }
    }
}

function Get-ExistingSecurityOptions {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if ($null -eq $config -or $null -eq $config.SecurityOptions) {
            return $null
        }

        return $config.SecurityOptions
    }
    catch {
        return $null
    }
}

function Get-InstallerResponseConfig {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Response file not found at '$Path'."
    }

    try {
        $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if ($null -eq $config) {
            throw 'Response file is empty.'
        }

        $schemaVersion = 1
        if ($config.PSObject.Properties.Match('schemaVersion').Count -gt 0 -and $null -ne $config.schemaVersion) {
            $schemaVersion = [int]$config.schemaVersion
        }

        if ($schemaVersion -ne 1) {
            throw "Unsupported response schemaVersion '$schemaVersion'. Supported versions: 1."
        }

        return $config
    }
    catch {
        throw "Failed to parse response file at '$Path'. Error: $($_.Exception.Message)"
    }
}

function Get-NestedValue {
    param(
        [object]$Root,
        [string[]]$Path
    )

    $current = $Root
    foreach ($segment in $Path) {
        if ($null -eq $current) {
            return $null
        }

        $property = $current.PSObject.Properties.Match($segment) | Select-Object -First 1
        if ($null -eq $property) {
            return $null
        }

        $current = $property.Value
    }

    return $current
}

function Get-ResponseString {
    param(
        [object]$Root,
        [string[]]$Path
    )

    $value = Get-NestedValue -Root $Root -Path $Path
    if ($null -eq $value) {
        return ''
    }

    return [string]$value
}

function Get-ResponseStringArray {
    param(
        [object]$Root,
        [string[]]$Path
    )

    $value = Get-NestedValue -Root $Root -Path $Path
    if ($null -eq $value) {
        return ,@()
    }

    $result = @($value | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    return ,$result
}

function Get-ResponseBoolean {
    param(
        [object]$Root,
        [string[]]$Path
    )

    $value = Get-NestedValue -Root $Root -Path $Path
    if ($null -eq $value) {
        return $null
    }

    if ($value -is [bool]) {
        return [bool]$value
    }

    $text = [string]$value
    if ($text.Equals('true', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    if ($text.Equals('false', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    throw "Response file value '$($Path -join '.')' must be boolean."
}

function Set-ObjectProperty {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Value
    )

    $property = $Object.PSObject.Properties.Match($Name) | Select-Object -First 1
    if ($null -eq $property) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
    else {
        $property.Value = $Value
    }
}

$certDirectory = Split-Path -Parent $CertificatePath
if (-not (Test-Path -LiteralPath $certDirectory)) {
    New-Item -ItemType Directory -Path $certDirectory -Force | Out-Null
}

if (-not $PSBoundParameters.ContainsKey('DnsNames')) {
    $DnsNames = Get-DefaultBootstrapDnsNames
}

if (-not $PSBoundParameters.ContainsKey('IpAddresses')) {
    $IpAddresses = Get-DefaultBootstrapIpAddresses
}

if ($WriteToAppSettings.IsPresent -and -not (Test-Path -LiteralPath $AppSettingsPath -PathType Leaf)) {
    throw "appsettings file not found at '$AppSettingsPath'."
}

$responseConfig = Get-InstallerResponseConfig -Path $ResponseFilePath

if ([string]::IsNullOrWhiteSpace($CertificateMode)) {
    $CertificateMode = Get-ResponseString -Root $responseConfig -Path @('security', 'certificateMode')
}

if ([string]::IsNullOrWhiteSpace($ProvidedCertificatePath)) {
    $ProvidedCertificatePath = Get-ResponseString -Root $responseConfig -Path @('security', 'providedCertificatePath')
}

if ([string]::IsNullOrWhiteSpace($ProvidedCertificatePassword)) {
    $ProvidedCertificatePassword = Get-ResponseString -Root $responseConfig -Path @('security', 'providedCertificatePassword')
}

if ([string]::IsNullOrWhiteSpace($CaCertificatePemPath)) {
    $CaCertificatePemPath = Get-ResponseString -Root $responseConfig -Path @('security', 'caCertificatePemPath')
}

if ([string]::IsNullOrWhiteSpace($CaCertificatePfxPath)) {
    $CaCertificatePfxPath = Get-ResponseString -Root $responseConfig -Path @('security', 'caCertificatePfxPath')
}

if ([string]::IsNullOrWhiteSpace($CaCertificatePemPath)) {
    $CaCertificatePemPath = Join-Path -Path $certDirectory -ChildPath "agent-ca.pem"
}

if ([string]::IsNullOrWhiteSpace($CaCertificatePfxPath)) {
    $CaCertificatePfxPath = Join-Path -Path $certDirectory -ChildPath "agent-ca.pfx"
}

if (-not $PSBoundParameters.ContainsKey('ApiKeyMode')) {
    $responseApiKeyMode = Get-ResponseString -Root $responseConfig -Path @('security', 'apiKeyMode')
    if (-not [string]::IsNullOrWhiteSpace($responseApiKeyMode)) {
        $ApiKeyMode = $responseApiKeyMode
    }
}

if ([string]::IsNullOrWhiteSpace($ProvidedApiKey)) {
    $ProvidedApiKey = Get-ResponseString -Root $responseConfig -Path @('security', 'providedApiKey')
}

if (-not $PSBoundParameters.ContainsKey('DnsNames')) {
    $responseDnsNames = Get-ResponseStringArray -Root $responseConfig -Path @('security', 'dnsNames')
    if ($responseDnsNames.Count -gt 0) {
        $DnsNames = $responseDnsNames
    }
}

if (-not $PSBoundParameters.ContainsKey('IpAddresses')) {
    $responseIpAddresses = Get-ResponseStringArray -Root $responseConfig -Path @('security', 'ipAddresses')
    if ($responseIpAddresses.Count -gt 0) {
        $IpAddresses = $responseIpAddresses
    }
}

$responseUrls = Get-ResponseString -Root $responseConfig -Path @('network', 'urls')
$responseAllowedHosts = Get-ResponseString -Root $responseConfig -Path @('network', 'allowedHosts')
$responseRequireHttps = Get-ResponseBoolean -Root $responseConfig -Path @('security', 'requireHttps')
$responseRequireApiKey = Get-ResponseBoolean -Root $responseConfig -Path @('security', 'requireApiKey')
$responseAllowedExecutablePaths = Get-ResponseStringArray -Root $responseConfig -Path @('agentOptions', 'allowedExecutablePaths')
$responseAllowedWritablePaths = Get-ResponseStringArray -Root $responseConfig -Path @('agentOptions', 'allowedWritablePaths')
$responseAllowedReadablePaths = Get-ResponseStringArray -Root $responseConfig -Path @('agentOptions', 'allowedReadablePaths')

$resolvedCertificateMode = Resolve-CertificateMode -RequestedMode $CertificateMode -UseCertificateAuthority $UseCertificateAuthority -BoundParameters $PSBoundParameters
$resolvedApiKeyMode = Resolve-ApiKeyMode -RequestedMode $ApiKeyMode
$existingSecurity = Get-ExistingSecurityOptions -Path $AppSettingsPath

$certificatePassword = $null
$caCertificatePassword = New-RandomString -ByteLength 24

if ($resolvedCertificateMode -eq 'Provided') {
    if ([string]::IsNullOrWhiteSpace($ProvidedCertificatePath)) {
        $ProvidedCertificatePath = $CertificatePath
    }

    if ([string]::IsNullOrWhiteSpace($ProvidedCertificatePath)) {
        throw 'Provided certificate mode requires -ProvidedCertificatePath (or -CertificatePath).'
    }

    if ([string]::IsNullOrWhiteSpace($ProvidedCertificatePassword)) {
        throw 'Provided certificate mode requires -ProvidedCertificatePassword.'
    }

    if (-not (Test-Path -LiteralPath $ProvidedCertificatePath -PathType Leaf)) {
        throw "Provided certificate file not found at '$ProvidedCertificatePath'."
    }

    try {
        [void](New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ProvidedCertificatePath, $ProvidedCertificatePassword))
    }
    catch {
        throw "Failed to load provided certificate from '$ProvidedCertificatePath'. Verify password and file integrity. Error: $($_.Exception.Message)"
    }

    $CertificatePath = $ProvidedCertificatePath
    $certificatePassword = $ProvidedCertificatePassword
    $UseCertificateAuthority = $false
}
else {
    $UseCertificateAuthority = ($resolvedCertificateMode -eq 'GeneratedCa')
}

$createCertificate = $resolvedCertificateMode -ne 'Provided' -and ($ForceRegenerate.IsPresent -or -not (Test-Path -LiteralPath $CertificatePath))

if ($resolvedCertificateMode -ne 'Provided') {
    if ($createCertificate) {
        $certificatePassword = New-RandomString -ByteLength 24
    }
    else {
        $existingCertificatePassword = if ($null -ne $existingSecurity) { [string]$existingSecurity.HttpsCertificatePassword } else { '' }
        $hasUsableExistingPassword = -not [string]::IsNullOrWhiteSpace($existingCertificatePassword) -and
            $existingCertificatePassword -ne 'CHANGE_ME_CERT_PASSWORD' -and
            $existingCertificatePassword -ne 'CHANGE_ME'

        if (-not $hasUsableExistingPassword) {
            $certificatePassword = New-RandomString -ByteLength 24
            Write-Warning "Existing certificate detected at '$CertificatePath' but no usable certificate password was found in appsettings. Generated a replacement password value."
        }
        else {
            $certificatePassword = $existingCertificatePassword
        }
    }
}

if ($resolvedApiKeyMode -eq 'Provided') {
    if ([string]::IsNullOrWhiteSpace($ProvidedApiKey)) {
        throw 'Provided API key mode requires -ProvidedApiKey.'
    }

    $apiKey = $ProvidedApiKey
}
else {
    $existingApiKey = if ($null -ne $existingSecurity) { [string]$existingSecurity.ApiKey } else { '' }
    if ($ForceRegenerate.IsPresent -or [string]::IsNullOrWhiteSpace($existingApiKey) -or $existingApiKey -eq 'CHANGE_ME') {
        $apiKey = New-RandomString -ByteLength 32
    }
    else {
        $apiKey = $existingApiKey
    }
}

$pfxPassword = ConvertTo-SecureString -String $certificatePassword -AsPlainText -Force
$caPfxPassword = ConvertTo-SecureString -String $caCertificatePassword -AsPlainText -Force

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
    if ($null -eq $config.SecurityOptions) {
        $config | Add-Member -NotePropertyName SecurityOptions -NotePropertyValue ([pscustomobject]@{})
    }
    if ($null -eq $config.AgentOptions) {
        $config | Add-Member -NotePropertyName AgentOptions -NotePropertyValue ([pscustomobject]@{})
    }

    $config.SecurityOptions.HttpsCertificatePath = $CertificatePath
    $config.SecurityOptions.HttpsCertificatePassword = $certificatePassword
    $config.SecurityOptions.ApiKey = $apiKey

    if (-not [string]::IsNullOrWhiteSpace($responseUrls)) {
        Set-ObjectProperty -Object $config -Name 'Urls' -Value $responseUrls
    }

    if (-not [string]::IsNullOrWhiteSpace($responseAllowedHosts)) {
        Set-ObjectProperty -Object $config -Name 'AllowedHosts' -Value $responseAllowedHosts
    }

    if ($null -ne $responseRequireHttps) {
        Set-ObjectProperty -Object $config.SecurityOptions -Name 'RequireHttps' -Value ([bool]$responseRequireHttps)
    }

    if ($null -ne $responseRequireApiKey) {
        Set-ObjectProperty -Object $config.SecurityOptions -Name 'RequireApiKey' -Value ([bool]$responseRequireApiKey)
    }

    if ($responseAllowedExecutablePaths.Count -gt 0) {
        Set-ObjectProperty -Object $config.AgentOptions -Name 'AllowedExecutablePaths' -Value @($responseAllowedExecutablePaths)
    }

    if ($responseAllowedWritablePaths.Count -gt 0) {
        Set-ObjectProperty -Object $config.AgentOptions -Name 'AllowedWritablePaths' -Value @($responseAllowedWritablePaths)
    }

    if ($responseAllowedReadablePaths.Count -gt 0) {
        Set-ObjectProperty -Object $config.AgentOptions -Name 'AllowedReadablePaths' -Value @($responseAllowedReadablePaths)
    }

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
        certificateMode = $resolvedCertificateMode
        apiKeyMode = $resolvedApiKeyMode
        responseFilePath = $ResponseFilePath
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
    if (Test-Path -LiteralPath $CertificatePath) {
        Write-Host "Importing certificate into LocalMachine\Root trust store..."
        try {
            if ($UseCertificateAuthority -and $createCertificate -and (Test-Path -LiteralPath $CaCertificatePfxPath)) {
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
    else {
        Write-Host "Skipping certificate trust: certificate file '$CertificatePath' was not found."
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
        $metadata = Get-FailureMetadata -ErrorMessage $_.Exception.Message

        $failure = @{
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
            error = $_.Exception.Message
            exceptionType = $_.Exception.GetType().FullName
            scriptPath = $PSCommandPath
            appSettingsPath = $AppSettingsPath
            certificatePath = $CertificatePath
            certificateMode = $resolvedCertificateMode
            apiKeyMode = $resolvedApiKeyMode
            responseFilePath = $ResponseFilePath
            secretHandoffPath = $SecretHandoffPath
            logPath = $LogPath
            errorCode = $metadata.errorCode
            suggestedAction = $metadata.suggestedAction
        }

        $failureJson = $failure | ConvertTo-Json -Depth 6
        $wrotePrimaryFailure = $false

        try {
            New-Item -ItemType Directory -Path $bootstrapDiagnosticsRoot -Force | Out-Null
            $failureJson | Set-Content -LiteralPath $bootstrapFailurePath -Encoding UTF8
            $failureOutputPath = $bootstrapFailurePath
            $wrotePrimaryFailure = $true
        }
        catch {
            # Fall back to a user-writable location when ProgramData is unavailable.
        }

        if (-not $wrotePrimaryFailure) {
            $failureJson | Set-Content -LiteralPath $bootstrapFailureFallbackPath -Encoding UTF8
            $failureOutputPath = $bootstrapFailureFallbackPath
        }
    }
    catch {
        # Ignore diagnostics-write errors and preserve original exception context.
    }

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Write-Error "Bootstrap provisioning failed. See '$LogPath' and '$failureOutputPath'. Error: $($_.Exception.Message)"
    }
    else {
        Write-Error "Bootstrap provisioning failed. See '$failureOutputPath'. Error: $($_.Exception.Message)"
    }

    throw
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Stop-Transcript | Out-Null
    }
}
