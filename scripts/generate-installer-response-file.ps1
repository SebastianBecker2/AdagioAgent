param(
    [string]$OutputPath = "installer-response.json",
    [switch]$NonInteractive,
    [ValidateSet('GeneratedCa', 'GeneratedLeaf', 'Provided')]
    [string]$CertificateMode = 'GeneratedCa',
    [string]$ProvidedCertificatePath = '',
    [string]$ProvidedCertificatePassword = '',
    [ValidateSet('Generate', 'Provided')]
    [string]$ApiKeyMode = 'Generate',
    [string]$ProvidedApiKey = '',
    [bool]$RequireHttps = $true,
    [bool]$RequireApiKey = $true,
    [string]$Urls = '',
    [string]$AllowedHosts = '',
    [string[]]$AllowedExecutablePaths = @('C:\Apps'),
    [string[]]$AllowedWritablePaths = @('C:\Apps'),
    [string[]]$AllowedReadablePaths = @('C:\Apps'),
    [string[]]$DnsNames = @(),
    [string[]]$IpAddresses = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DiscoveredIpAddresses {
    $addresses = @('127.0.0.1')

    try {
        $allNics = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()
        foreach ($nic in $allNics) {
            if ($nic.OperationalStatus -ne [System.Net.NetworkInformation.OperationalStatus]::Up) {
                continue
            }

            foreach ($unicast in $nic.GetIPProperties().UnicastAddresses) {
                $address = $unicast.Address
                if ($null -eq $address) {
                    continue
                }

                if ($address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
                    continue
                }

                if ([System.Net.IPAddress]::IsLoopback($address)) {
                    continue
                }

                $text = $address.ToString()
                if ($text.StartsWith('169.254.', [System.StringComparison]::Ordinal)) {
                    continue
                }

                $addresses += $text
            }
        }
    }
    catch {
        # Keep loopback only when adapter discovery is unavailable.
    }

    return @($addresses | Select-Object -Unique)
}

function Read-HostOrDefault {
    param(
        [string]$Prompt,
        [string]$DefaultValue
    )

    $suffix = if ([string]::IsNullOrWhiteSpace($DefaultValue)) { '' } else { " [$DefaultValue]" }
    $value = Read-Host "$Prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value.Trim()
}

function Read-BoolOrDefault {
    param(
        [string]$Prompt,
        [bool]$DefaultValue
    )

    $defaultText = if ($DefaultValue) { 'true' } else { 'false' }
    while ($true) {
        $raw = Read-HostOrDefault -Prompt "$Prompt (true/false)" -DefaultValue $defaultText
        if ($raw -match '^(?i:true|false)$') {
            return $raw.Equals('true', [System.StringComparison]::OrdinalIgnoreCase)
        }

        Write-Warning "Please enter 'true' or 'false'."
    }
}

function Split-ListInput {
    param([string]$InputText)

    if ([string]::IsNullOrWhiteSpace($InputText)) {
        return @()
    }

    return @($InputText.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

$discoveredIpAddresses = Get-DiscoveredIpAddresses
$discoveredHostName = $env:COMPUTERNAME

if ($DnsNames.Count -eq 0) {
    $DnsNames = @($discoveredHostName, 'localhost' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

if ($IpAddresses.Count -eq 0) {
    $IpAddresses = @($discoveredIpAddresses)
}

if ([string]::IsNullOrWhiteSpace($Urls)) {
    $primaryIp = if ($IpAddresses.Count -gt 0) { $IpAddresses[0] } else { '127.0.0.1' }
    $Urls = "https://$primaryIp`:5443"
}

if ([string]::IsNullOrWhiteSpace($AllowedHosts)) {
    $allowedHostValues = @('localhost', '127.0.0.1')
    if (-not [string]::IsNullOrWhiteSpace($discoveredHostName)) {
        $allowedHostValues += $discoveredHostName
    }
    $allowedHostValues += $IpAddresses
    $AllowedHosts = (@($allowedHostValues | Select-Object -Unique) -join ';')
}

if (-not $NonInteractive.IsPresent) {
    Write-Host '--- Adagio Installer Response File Wizard ---'
    Write-Host 'Press Enter to keep defaults shown in brackets.'
    Write-Host ''

    $CertificateMode = Read-HostOrDefault -Prompt 'Certificate mode (GeneratedCa, GeneratedLeaf, Provided)' -DefaultValue $CertificateMode
    if (@('GeneratedCa', 'GeneratedLeaf', 'Provided') -notcontains $CertificateMode) {
        throw "CertificateMode must be GeneratedCa, GeneratedLeaf, or Provided."
    }

    if ($CertificateMode -eq 'Provided') {
        $ProvidedCertificatePath = Read-HostOrDefault -Prompt 'Provided certificate path (.pfx)' -DefaultValue $ProvidedCertificatePath
        $ProvidedCertificatePassword = Read-HostOrDefault -Prompt 'Provided certificate password' -DefaultValue $ProvidedCertificatePassword
    }

    $ApiKeyMode = Read-HostOrDefault -Prompt 'API key mode (Generate, Provided)' -DefaultValue $ApiKeyMode
    if (@('Generate', 'Provided') -notcontains $ApiKeyMode) {
        throw "ApiKeyMode must be Generate or Provided."
    }

    if ($ApiKeyMode -eq 'Provided') {
        $ProvidedApiKey = Read-HostOrDefault -Prompt 'Provided API key' -DefaultValue $ProvidedApiKey
    }

    $RequireHttps = Read-BoolOrDefault -Prompt 'Require HTTPS' -DefaultValue $RequireHttps
    $RequireApiKey = Read-BoolOrDefault -Prompt 'Require API key' -DefaultValue $RequireApiKey

    $Urls = Read-HostOrDefault -Prompt 'URLs value' -DefaultValue $Urls
    $AllowedHosts = Read-HostOrDefault -Prompt 'AllowedHosts value (semicolon-separated)' -DefaultValue $AllowedHosts

    $dnsInput = Read-HostOrDefault -Prompt 'DNS names (comma-separated)' -DefaultValue ($DnsNames -join ',')
    $ipInput = Read-HostOrDefault -Prompt 'IP addresses (comma-separated)' -DefaultValue ($IpAddresses -join ',')
    $execInput = Read-HostOrDefault -Prompt 'Allowed executable paths (comma-separated)' -DefaultValue ($AllowedExecutablePaths -join ',')
    $writeInput = Read-HostOrDefault -Prompt 'Allowed writable paths (comma-separated)' -DefaultValue ($AllowedWritablePaths -join ',')
    $readInput = Read-HostOrDefault -Prompt 'Allowed readable paths (comma-separated)' -DefaultValue ($AllowedReadablePaths -join ',')

    $DnsNames = Split-ListInput -InputText $dnsInput
    $IpAddresses = Split-ListInput -InputText $ipInput
    $AllowedExecutablePaths = Split-ListInput -InputText $execInput
    $AllowedWritablePaths = Split-ListInput -InputText $writeInput
    $AllowedReadablePaths = Split-ListInput -InputText $readInput
}

if ($CertificateMode -eq 'Provided') {
    if ([string]::IsNullOrWhiteSpace($ProvidedCertificatePath)) {
        throw 'Provided certificate mode requires a non-empty providedCertificatePath.'
    }

    if ([string]::IsNullOrWhiteSpace($ProvidedCertificatePassword)) {
        throw 'Provided certificate mode requires a non-empty providedCertificatePassword.'
    }
}

if ($ApiKeyMode -eq 'Provided' -and [string]::IsNullOrWhiteSpace($ProvidedApiKey)) {
    throw 'Provided API key mode requires a non-empty providedApiKey.'
}

if ($AllowedExecutablePaths.Count -eq 0) {
    throw 'allowedExecutablePaths must contain at least one entry.'
}

if ($AllowedWritablePaths.Count -eq 0) {
    throw 'allowedWritablePaths must contain at least one entry.'
}

if ($AllowedReadablePaths.Count -eq 0) {
    throw 'allowedReadablePaths must contain at least one entry.'
}

if ($DnsNames.Count -eq 0 -and $IpAddresses.Count -eq 0) {
    throw 'At least one DNS name or IP address must be provided.'
}

$payload = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
    security = [ordered]@{
        certificateMode = $CertificateMode
        providedCertificatePath = $ProvidedCertificatePath
        providedCertificatePassword = $ProvidedCertificatePassword
        apiKeyMode = $ApiKeyMode
        providedApiKey = $ProvidedApiKey
        requireHttps = $RequireHttps
        requireApiKey = $RequireApiKey
        dnsNames = @($DnsNames)
        ipAddresses = @($IpAddresses)
    }
    network = [ordered]@{
        urls = $Urls
        allowedHosts = $AllowedHosts
    }
    agentOptions = [ordered]@{
        allowedExecutablePaths = @($AllowedExecutablePaths)
        allowedWritablePaths = @($AllowedWritablePaths)
        allowedReadablePaths = @($AllowedReadablePaths)
    }
    discovery = [ordered]@{
        hostName = $discoveredHostName
        discoveredIpAddresses = @($discoveredIpAddresses)
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$payload | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

Write-Host "Installer response file written: $OutputPath"
Write-Host "Certificate mode: $CertificateMode"
Write-Host "API key mode: $ApiKeyMode"
