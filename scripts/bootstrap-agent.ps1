param(
    [string]$CertificatePath = "C:\ProgramData\AdagioMachineAgent\tls\agent.pfx",
    [switch]$PersistToEnvironment,
    [switch]$WriteToAppSettings,
    [string]$AppSettingsPath = "machine-agent\appsettings.json",
    [switch]$ForceRegenerate,
    [switch]$SuppressSecretOutput,
    [string]$LogPath = ""
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

$certDirectory = Split-Path -Parent $CertificatePath
if (-not (Test-Path -LiteralPath $certDirectory)) {
    New-Item -ItemType Directory -Path $certDirectory -Force | Out-Null
}

$certificatePassword = New-RandomString -ByteLength 24
$apiKey = New-RandomString -ByteLength 32

$pfxPassword = ConvertTo-SecureString -String $certificatePassword -AsPlainText -Force
$createCertificate = $ForceRegenerate.IsPresent -or -not (Test-Path -LiteralPath $CertificatePath)

if ($createCertificate) {
    $candidateStores = @(
        "Cert:\LocalMachine\My",
        "Cert:\CurrentUser\My"
    )

    $cert = $null
    $selectedStore = $null
    $lastError = $null

    foreach ($store in $candidateStores) {
        try {
            $newCertParams = @{
                DnsName           = @("localhost", "127.0.0.1")
                CertStoreLocation = $store
                FriendlyName      = "AdagioMachineAgent Bootstrap"
                NotAfter          = (Get-Date).AddYears(2)
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
