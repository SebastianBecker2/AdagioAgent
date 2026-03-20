param(
    [string]$AppSettingsPath = "appsettings.json",
    [string]$LogPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not [System.IO.Path]::IsPathRooted($AppSettingsPath)) {
    $coLocatedAppSettings = Join-Path -Path (Split-Path -Parent $PSCommandPath) -ChildPath "appsettings.json"
    if ((Test-Path -LiteralPath $coLocatedAppSettings -PathType Leaf) -and -not (Test-Path -LiteralPath $AppSettingsPath -PathType Leaf)) {
        $AppSettingsPath = $coLocatedAppSettings
    }
}

$diagnosticsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) "AdagioMachineAgent"
$failurePath = Join-Path $diagnosticsRoot "bootstrap-preflight-failure.json"

function Get-FailureMetadata {
    param(
        [string]$ErrorMessage
    )

    if ($ErrorMessage -match 'CHANGE_ME') {
        return @{
            errorCode = 'AA2001'
            suggestedAction = 'Re-run installer to regenerate bootstrap values or update appsettings.json with real security values.'
        }
    }

    if ($ErrorMessage -match 'certificate file not found') {
        return @{
            errorCode = 'AA2002'
            suggestedAction = 'Verify SecurityOptions.HttpsCertificatePath points to an existing .pfx file and rerun installation.'
        }
    }

    if ($ErrorMessage -match 'Failed to load HTTPS certificate') {
        return @{
            errorCode = 'AA2003'
            suggestedAction = 'Verify SecurityOptions.HttpsCertificatePassword matches the .pfx file password and rerun installation.'
        }
    }

    if ($ErrorMessage -match 'ApiKey is required') {
        return @{
            errorCode = 'AA2004'
            suggestedAction = 'Set a non-empty SecurityOptions.ApiKey value and rerun installation.'
        }
    }

    if ($ErrorMessage -match 'AllowedHosts|AllowedExecutablePaths|AllowedWritablePaths|AllowedReadablePaths') {
        return @{
            errorCode = 'AA2005'
            suggestedAction = 'Ensure installer wizard or response-file path settings include non-empty allowed hosts and path allowlists.'
        }
    }

    if ($ErrorMessage -match 'Urls') {
        return @{
            errorCode = 'AA2006'
            suggestedAction = 'Set Urls to valid endpoint values and ensure HTTPS endpoints are used when RequireHttps is true.'
        }
    }

    return @{
        errorCode = 'AA2099'
        suggestedAction = 'Inspect bootstrap-preflight.log for detailed validation output, then retry installation.'
    }
}

if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
    $logDir = Split-Path -Parent $LogPath
    if (-not [string]::IsNullOrWhiteSpace($logDir) -and -not (Test-Path -LiteralPath $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }

    Start-Transcript -LiteralPath $LogPath -Force | Out-Null
}

try {
    if (-not (Test-Path -LiteralPath $AppSettingsPath -PathType Leaf)) {
        throw "appsettings file not found at '$AppSettingsPath'."
    }

    $config = Get-Content -LiteralPath $AppSettingsPath -Raw | ConvertFrom-Json
    if (-not $config.SecurityOptions) {
        throw "Missing SecurityOptions section in appsettings."
    }

    $security = $config.SecurityOptions
    $urls = [string]$config.Urls
    $allowedHosts = [string]$config.AllowedHosts
    $agentOptions = $config.AgentOptions

    if ([string]::IsNullOrWhiteSpace($urls)) {
        throw 'Urls is required but empty.'
    }

    $urlEntries = @($urls.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($urlEntries.Count -eq 0) {
        throw 'Urls does not contain any valid endpoint entries.'
    }

    if ([string]::IsNullOrWhiteSpace($allowedHosts)) {
        throw 'AllowedHosts is required but empty.'
    }

    if ($null -eq $agentOptions) {
        throw 'Missing AgentOptions section in appsettings.'
    }

    if (@($agentOptions.AllowedExecutablePaths).Count -eq 0) {
        throw 'AllowedExecutablePaths is empty.'
    }

    if (@($agentOptions.AllowedWritablePaths).Count -eq 0) {
        throw 'AllowedWritablePaths is empty.'
    }

    if (@($agentOptions.AllowedReadablePaths).Count -eq 0) {
        throw 'AllowedReadablePaths is empty.'
    }

    if ($security.RequireApiKey -and [string]::IsNullOrWhiteSpace([string]$security.ApiKey)) {
        throw "SecurityOptions.ApiKey is required but empty."
    }

    if ($security.RequireApiKey -and [string]$security.ApiKey -eq "CHANGE_ME") {
        throw "SecurityOptions.ApiKey is still placeholder value CHANGE_ME."
    }

    if ($security.RequireHttps) {
        foreach ($urlEntry in $urlEntries) {
            if (-not $urlEntry.StartsWith('https://', [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Urls contains non-HTTPS endpoint '$urlEntry' while SecurityOptions.RequireHttps is true."
            }
        }

        $certPath = [string]$security.HttpsCertificatePath
        $certPassword = [string]$security.HttpsCertificatePassword

        if ([string]::IsNullOrWhiteSpace($certPath)) {
            throw "SecurityOptions.HttpsCertificatePath is required but empty."
        }

        if ([string]::IsNullOrWhiteSpace($certPassword)) {
            throw "SecurityOptions.HttpsCertificatePassword is required but empty."
        }

        if ($certPassword -eq "CHANGE_ME_CERT_PASSWORD") {
            throw "SecurityOptions.HttpsCertificatePassword is still placeholder value CHANGE_ME_CERT_PASSWORD."
        }

        if (-not (Test-Path -LiteralPath $certPath -PathType Leaf)) {
            throw "HTTPS certificate file not found at '$certPath'."
        }

        try {
            [void](New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath, $certPassword))
        }
        catch {
            throw "Failed to load HTTPS certificate from '$certPath'. Verify certificate password and file integrity. Error: $($_.Exception.Message)"
        }
    }

    Write-Host "Bootstrap preflight passed."
}
catch {
    try {
        New-Item -ItemType Directory -Path $diagnosticsRoot -Force | Out-Null
        $metadata = Get-FailureMetadata -ErrorMessage $_.Exception.Message

        $failure = @{
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
            error = $_.Exception.Message
            exceptionType = $_.Exception.GetType().FullName
            appSettingsPath = $AppSettingsPath
            logPath = $LogPath
            errorCode = $metadata.errorCode
            suggestedAction = $metadata.suggestedAction
        }

        $failure | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $failurePath -Encoding UTF8
    }
    catch {
        # Best effort only.
    }

    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Write-Error "Bootstrap preflight failed. See '$LogPath' and '$failurePath'. Error: $($_.Exception.Message)"
    }
    else {
        Write-Error "Bootstrap preflight failed. See '$failurePath'. Error: $($_.Exception.Message)"
    }

    throw
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Stop-Transcript | Out-Null
    }
}
