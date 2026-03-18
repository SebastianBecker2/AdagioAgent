param(
    [string]$CertificatePath = "C:\ProgramData\AdagioMachineAgent\tls\agent.pfx",
    [switch]$PersistToEnvironment,
    [switch]$WriteToAppSettings,
    [string]$AppSettingsPath = "machine-agent\appsettings.json",
    [switch]$ForceRegenerate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-RandomString {
    param(
        [int]$ByteLength = 32
    )

    $bytes = New-Object byte[] $ByteLength
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

$certDirectory = Split-Path -Parent $CertificatePath
if (-not (Test-Path -LiteralPath $certDirectory)) {
    New-Item -ItemType Directory -Path $certDirectory -Force | Out-Null
}

$certificatePassword = New-RandomString -ByteLength 24
$apiKey = New-RandomString -ByteLength 32

$pfxPassword = ConvertTo-SecureString -String $certificatePassword -AsPlainText -Force
$createCertificate = $ForceRegenerate.IsPresent -or -not (Test-Path -LiteralPath $CertificatePath)

if ($createCertificate) {
    $newCertParams = @{
        DnsName           = @("localhost", "127.0.0.1")
        CertStoreLocation = "Cert:\CurrentUser\My"
        FriendlyName      = "AdagioMachineAgent Bootstrap"
        NotAfter          = (Get-Date).AddYears(2)
    }

    $cert = New-SelfSignedCertificate @newCertParams

    $exportParams = @{
        Cert     = "Cert:\CurrentUser\My\$($cert.Thumbprint)"
        FilePath = $CertificatePath
        Password = $pfxPassword
        Force    = $true
    }

    Export-PfxCertificate @exportParams | Out-Null
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

Write-Host "Bootstrap completed."
Write-Host "Certificate path: $CertificatePath"
Write-Host "API key generated (hidden)."
Write-Host "Persisted to user environment: $($PersistToEnvironment.IsPresent)"
Write-Host "Written to appsettings: $($WriteToAppSettings.IsPresent)"
Write-Host ""
Write-Host "Next steps:"
Write-Host "1. Restart the AdagioMachineAgent service after updating config/environment."
Write-Host "2. Configure adagioAgent.vmAgentApiKey in VS Code with the generated key value."
Write-Host ""
Write-Host "Generated values (copy securely now):"
Write-Host "SecurityOptions__ApiKey=$apiKey"
Write-Host "SecurityOptions__HttpsCertificatePassword=$certificatePassword"
