[CmdletBinding()]
param(
    [string]$MsiPath = "installer\bin\x64\Release\AdagioMachineAgentSetup.msi",
    [string]$PreviousMsiPath = "",
    [string]$OutputDir = "artifacts\installer-validation",
    [string[]]$ScenarioNames = @("FreshSilentInstall"),
    [string]$InstallDirectory = "${env:ProgramFiles}\AdagioMachineAgent",
    [string]$ServiceName = "AdagioMachineAgent",
    [string]$BaseUrl = "https://127.0.0.1:5443",
    [int]$ServiceStartTimeoutSeconds = 120,
    [switch]$ForceCleanMachine,
    [switch]$KeepInstalled,
    [switch]$FailOnScenarioFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

function Resolve-RepoRelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

$MsiPath = Resolve-RepoRelativePath -Path $MsiPath
$PreviousMsiPath = Resolve-RepoRelativePath -Path $PreviousMsiPath
$OutputDir = Resolve-RepoRelativePath -Path $OutputDir

$supportedScenarios = @('FreshSilentInstall', 'AdjacentUpgrade')
$diagnosticsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'AdagioMachineAgent'
$handoffPath = Join-Path $diagnosticsRoot 'bootstrap-secrets.json'

function Test-IsAdministrator {
    $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($currentIdentity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Initialize-CertificateBypass {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12

    # Use a compiled .NET delegate rather than a PowerShell scriptblock.
    # HttpWebRequest invokes the validation callback on a thread-pool thread that
    # carries no PowerShell runspace; a scriptblock delegate throws in that context
    # and causes the TLS handshake to abort with "connection was closed: unexpected
    # error on send".  A pure C# anonymous method avoids that runspace dependency.
    if (-not ([System.Management.Automation.PSTypeName]'AdagioTrustAllCerts').Type) {
        Add-Type -TypeDefinition @"
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public class AdagioTrustAllCerts {
    public static void Apply() {
        ServicePointManager.ServerCertificateValidationCallback =
            new RemoteCertificateValidationCallback(
                delegate(object s, X509Certificate c, X509Chain ch, SslPolicyErrors e) {
                    return true;
                });
    }
}
"@
    }
    [AdagioTrustAllCerts]::Apply()
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

function Get-TextHash {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ''
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return [System.BitConverter]::ToString($hashBytes).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Invoke-AgentRequest {
    param(
        [string]$RelativePath,
        [string]$ApiKey,
        [string]$ApiKeyHeaderName = 'X-API-Key',
        [int]$MaxAttempts = 6,
        [int]$RetryDelaySeconds = 5
    )

    Initialize-CertificateBypass

    $uri = '{0}{1}' -f $BaseUrl.TrimEnd('/'), $RelativePath
    $lastError = $null
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            return Invoke-RestMethod -Uri $uri -Headers @{ $ApiKeyHeaderName = $ApiKey } -Method Get -TimeoutSec 20 -UseBasicParsing
        }
        catch {
            $lastError = $_
            if ($attempt -lt $MaxAttempts) {
                Start-Sleep -Seconds $RetryDelaySeconds
            }
        }
    }
    throw $lastError.Exception
}

function Invoke-Msiexec {
    param(
        [ValidateSet('install', 'uninstall')]
        [string]$Mode,
        [string]$PackagePath,
        [string]$LogPath
    )

    $operation = if ($Mode -eq 'install') { '/i' } else { '/x' }
    if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
        throw "msiexec $Mode package not found at '$PackagePath'."
    }

    $resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
    $resolvedLogPath = [System.IO.Path]::GetFullPath($LogPath)
    $logDirectory = Split-Path -Path $resolvedLogPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    }

    $arguments = @(
        $operation,
        $resolvedPackagePath,
        '/qn',
        '/norestart',
        '/l*v',
        $resolvedLogPath
    )

    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -PassThru -Wait -NoNewWindow
    return $process.ExitCode
}

function Wait-ForServiceStatus {
    param(
        [string]$Name,
        [ValidateSet('Running', 'Absent')]
        [string]$DesiredStatus,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $service = Get-Service -Name $Name -ErrorAction Stop
        }
        catch {
            $service = $null
        }

        if ($DesiredStatus -eq 'Absent') {
            if (-not $service) {
                return $true
            }
        }
        elseif ($service -and $service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
            return $true
        }

        Start-Sleep -Seconds 2
    }
    while ((Get-Date) -lt $deadline)

    return $false
}

function Get-ServiceState {
    param([string]$Name)

    try {
        return (Get-Service -Name $Name -ErrorAction Stop).Status.ToString()
    }
    catch {
        return 'Absent'
    }
}

function Get-FileSnapshot {
    param(
        [string]$Path,
        [datetime]$ScenarioStartTimeUtc
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{
            exists = $false
            path = $Path
            updatedDuringScenario = $false
        }
    }

    $item = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        exists = $true
        path = $Path
        lastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
        updatedDuringScenario = $item.LastWriteTimeUtc -ge $ScenarioStartTimeUtc.AddSeconds(-1)
    }
}

function Copy-ArtifactIfFresh {
    param(
        [string]$SourcePath,
        [string]$DestinationDirectory,
        [datetime]$ScenarioStartTimeUtc
    )

    $snapshot = Get-FileSnapshot -Path $SourcePath -ScenarioStartTimeUtc $ScenarioStartTimeUtc
    if (-not $snapshot.exists -or -not $snapshot.updatedDuringScenario) {
        return $snapshot
    }

    Copy-Item -LiteralPath $SourcePath -Destination (Join-Path $DestinationDirectory (Split-Path -Leaf $SourcePath)) -Force
    $snapshot | Add-Member -NotePropertyName copiedToOutput -NotePropertyValue $true
    return $snapshot
}

function Get-MsiPropertyValue {
    param(
        [string]$PackagePath,
        [string]$PropertyName
    )

    $installer = $null
    $database = $null
    $view = $null
    $record = $null

    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.GetType().InvokeMember('OpenDatabase', [System.Reflection.BindingFlags]::InvokeMethod, $null, $installer, @($PackagePath, 0))
        $query = "SELECT `Value` FROM `Property` WHERE `Property`='$PropertyName'"
        $view = $database.GetType().InvokeMember('OpenView', [System.Reflection.BindingFlags]::InvokeMethod, $null, $database, @($query))
        $view.GetType().InvokeMember('Execute', [System.Reflection.BindingFlags]::InvokeMethod, $null, $view, $null) | Out-Null
        $record = $view.GetType().InvokeMember('Fetch', [System.Reflection.BindingFlags]::InvokeMethod, $null, $view, $null)
        if (-not $record) {
            return ''
        }

        return [string]$record.StringData(1)
    }
    finally {
        foreach ($comObject in @($record, $view, $database, $installer)) {
            if ($comObject) {
                [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($comObject)
            }
        }
    }
}

function Get-MsiProductVersion {
    param([string]$PackagePath)

    return Get-MsiPropertyValue -PackagePath $PackagePath -PropertyName 'ProductVersion'
}

function Assert-CleanMachine {
    if ((Get-ServiceState -Name $ServiceName) -ne 'Absent') {
        throw "Service '$ServiceName' is already installed. Refusing to modify a non-clean machine."
    }

    if (Test-Path -LiteralPath $InstallDirectory -PathType Container) {
        throw "Install directory '$InstallDirectory' already exists. Refusing to modify a non-clean machine."
    }
}

function Get-InstalledProductCodes {
    $candidateRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    $results = [System.Collections.Generic.List[string]]::new()
    foreach ($root in $candidateRoots) {
        $entries = Get-ItemProperty -Path $root -ErrorAction SilentlyContinue
        foreach ($entry in $entries) {
            $displayName = [string]$entry.DisplayName
            if ($displayName -ne 'Adagio Machine Agent') {
                continue
            }

            $productCode = $null
            $keyName = [string]$entry.PSChildName
            if ($keyName -match '^\{[0-9A-Fa-f\-]+\}$') {
                $productCode = $keyName
            }
            elseif (-not [string]::IsNullOrWhiteSpace([string]$entry.UninstallString) -and [string]$entry.UninstallString -match '(\{[0-9A-Fa-f\-]+\})') {
                $productCode = $matches[1]
            }

            if (-not [string]::IsNullOrWhiteSpace($productCode)) {
                [void]$results.Add($productCode)
            }
        }
    }

    return @($results | Select-Object -Unique)
}

function Invoke-ProductCodeUninstall {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProductCode,
        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    $arguments = @(
        '/x',
        $ProductCode,
        '/qn',
        '/norestart',
        '/l*v',
        $LogPath
    )

    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -PassThru -Wait -NoNewWindow
    return $process.ExitCode
}

function Ensure-CleanMachine {
    param(
        [string]$ScenarioOutputDir,
        [string]$ScenarioName
    )

    $serviceState = Get-ServiceState -Name $ServiceName
    $installDirExists = Test-Path -LiteralPath $InstallDirectory -PathType Container
    if ($serviceState -eq 'Absent' -and -not $installDirExists) {
        return
    }

    Write-Host "Force-cleaning machine state before scenario '$ScenarioName'. ServiceState=$serviceState InstallDirectoryExists=$installDirExists"
    $precleanLogDir = Join-Path $ScenarioOutputDir 'preclean'
    New-Item -ItemType Directory -Path $precleanLogDir -Force | Out-Null

    $productCodes = Get-InstalledProductCodes
    foreach ($productCode in $productCodes) {
        $safeProductCode = ($productCode -replace '[{}]', '')
        $logPath = Join-Path $precleanLogDir ("uninstall-$safeProductCode.log")
        $exitCode = Invoke-ProductCodeUninstall -ProductCode $productCode -LogPath $logPath
        if ($exitCode -ne 0 -and $exitCode -ne 1605) {
            throw "Pre-clean uninstall failed for product code '$productCode' with exit code $exitCode."
        }
    }

    if ((Get-ServiceState -Name $ServiceName) -ne 'Absent') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        & sc.exe delete $ServiceName | Out-Null
    }

    if (-not (Wait-ForServiceStatus -Name $ServiceName -DesiredStatus Absent -TimeoutSeconds 60)) {
        throw "Service '$ServiceName' still exists after force-clean attempt."
    }

    if (Test-Path -LiteralPath $InstallDirectory -PathType Container) {
        Remove-Item -LiteralPath $InstallDirectory -Recurse -Force -ErrorAction Stop
    }
}

function Get-InstalledAgentSnapshot {
    param(
        [string]$InstalledAppSettingsPath,
        [string]$ExpectedApiKey = ''
    )

    if (-not (Test-Path -LiteralPath $InstalledAppSettingsPath -PathType Leaf)) {
        throw "Installed appsettings.json not found at '$InstalledAppSettingsPath'."
    }

    $appSettings = Get-Content -LiteralPath $InstalledAppSettingsPath -Raw | ConvertFrom-Json
    $securityOptions = $appSettings.SecurityOptions
    if (-not $securityOptions) {
        throw 'Installed appsettings.json is missing the SecurityOptions section.'
    }

    $appSettingsApiKey = [string]$securityOptions.ApiKey
    $appSettingsCertificatePassword = [string]$securityOptions.HttpsCertificatePassword
    $certificatePath = [string]$securityOptions.HttpsCertificatePath

    $apiKeyConfigured = -not [string]::IsNullOrWhiteSpace($appSettingsApiKey) -and $appSettingsApiKey -ne 'CHANGE_ME'
    $certificatePasswordConfigured = -not [string]::IsNullOrWhiteSpace($appSettingsCertificatePassword) -and $appSettingsCertificatePassword -ne 'CHANGE_ME_CERT_PASSWORD'
    $certificateExists = -not [string]::IsNullOrWhiteSpace($certificatePath) -and (Test-Path -LiteralPath $certificatePath -PathType Leaf)

    if (-not $apiKeyConfigured) {
        throw 'Installed appsettings.json still contains a placeholder or empty API key.'
    }

    if (-not $certificatePasswordConfigured) {
        throw 'Installed appsettings.json still contains a placeholder or empty certificate password.'
    }

    if (-not $certificateExists) {
        throw "Configured certificate file '$certificatePath' does not exist after install."
    }

    if (-not (Test-Path -LiteralPath $handoffPath -PathType Leaf)) {
        throw "Bootstrap secret handoff file was not created at '$handoffPath'."
    }

    $handoff = Get-Content -LiteralPath $handoffPath -Raw | ConvertFrom-Json
    $handoffApiKey = [string]$handoff.apiKey
    $handoffCertificatePassword = [string]$handoff.httpsCertificatePassword
    $handoffAcl = Get-Acl -LiteralPath $handoffPath
    $handoffSidValues = @($handoffAcl.Access | ForEach-Object { Convert-IdentityToSid -IdentityReference $_.IdentityReference } | Sort-Object -Unique)
    $expectedHandoffSids = @('S-1-5-18', 'S-1-5-32-544')

    if ([string]::IsNullOrWhiteSpace($handoffApiKey)) {
        throw 'Bootstrap secret handoff file did not contain an API key.'
    }

    if ([string]::IsNullOrWhiteSpace($handoffCertificatePassword)) {
        throw 'Bootstrap secret handoff file did not contain a certificate password.'
    }

    if ([string]$handoff.httpsCertificatePath -ne $certificatePath) {
        throw 'Bootstrap secret handoff certificate path does not match installed appsettings.json.'
    }

    if (-not $handoffAcl.AreAccessRulesProtected) {
        throw 'Bootstrap secret handoff file ACL is not protected.'
    }

    if (@($handoffSidValues | Where-Object { $expectedHandoffSids -notcontains $_ }).Count -ne 0 -or @($expectedHandoffSids | Where-Object { $handoffSidValues -notcontains $_ }).Count -ne 0) {
        throw 'Bootstrap secret handoff file ACL does not match the expected SYSTEM and Administrators-only access.'
    }

    $apiKeyForProbe = if (-not [string]::IsNullOrWhiteSpace($ExpectedApiKey)) { $ExpectedApiKey } else { $handoffApiKey }
    $health = Invoke-AgentRequest -RelativePath '/health' -ApiKey $apiKeyForProbe
    $ready = Invoke-AgentRequest -RelativePath '/ready' -ApiKey $apiKeyForProbe
    $diagnostics = Invoke-AgentRequest -RelativePath '/diagnostics/status' -ApiKey $apiKeyForProbe
    $exportMetadata = Invoke-AgentRequest -RelativePath '/diagnostics/export-metadata' -ApiKey $apiKeyForProbe

    if ([string]$health.status -ne 'healthy') {
        throw "Health endpoint returned unexpected status '$($health.status)'."
    }

    if ([int]$health.apiVersion -ne 1 -or [int]$ready.apiVersion -ne 1 -or [int]$diagnostics.apiVersion -ne 1) {
        throw 'One or more installer validation endpoint probes returned an unexpected API version.'
    }

    if ([string]$exportMetadata.apiKeyHeaderName -ne 'X-API-Key') {
        throw 'Diagnostics export metadata reported an unexpected API key header name.'
    }

    if (-not [bool]$exportMetadata.httpsRequired -or -not [bool]$exportMetadata.apiKeyRequired) {
        throw 'Diagnostics export metadata reported unexpected transport security settings.'
    }

    return [pscustomobject]@{
        publicAppSettings = [pscustomobject]@{
            exists = $true
            path = $InstalledAppSettingsPath
            apiKeyConfigured = $apiKeyConfigured
            certificatePasswordConfigured = $certificatePasswordConfigured
            certificatePath = $certificatePath
            certificateExists = $certificateExists
        }
        publicHandoff = [pscustomobject]@{
            exists = $true
            path = $handoffPath
            apiKeyPresent = $true
            certificatePasswordPresent = $true
            certificatePathMatchesAppSettings = $true
            aclProtected = $handoffAcl.AreAccessRulesProtected
            allowedSidValues = $handoffSidValues
        }
        publicDiagnostics = [pscustomobject]@{
            healthApiVersion = $health.apiVersion
            readyApiVersion = $ready.apiVersion
            diagnosticsApiVersion = $diagnostics.apiVersion
            healthVersion = [string]$health.version
            readyVersion = [string]$ready.version
            diagnosticsVersion = [string]$diagnostics.version
            exportMetadataApiKeyHeaderName = [string]$exportMetadata.apiKeyHeaderName
            exportMetadataHttpsRequired = [bool]$exportMetadata.httpsRequired
            exportMetadataApiKeyRequired = [bool]$exportMetadata.apiKeyRequired
            readinessIssueCount = @($ready.issues).Count
        }
        healthStatus = [string]$health.status
        readinessStatus = [string]$ready.status
        diagnosticsStatus = [string]$diagnostics.status
        internal = [pscustomobject]@{
            probeApiKey = $apiKeyForProbe
            appSettingsApiKeyHash = Get-TextHash -Value $appSettingsApiKey
            appSettingsCertificatePasswordHash = Get-TextHash -Value $appSettingsCertificatePassword
            handoffApiKeyHash = Get-TextHash -Value $handoffApiKey
            handoffCertificatePasswordHash = Get-TextHash -Value $handoffCertificatePassword
        }
    }
}

function Write-SummaryArtifacts {
    param(
        [pscustomobject]$Summary,
        [string]$DestinationDirectory
    )

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null

    $jsonPath = Join-Path $DestinationDirectory 'installer-validation-summary.json'
    $markdownPath = Join-Path $DestinationDirectory 'installer-validation-summary.md'

    $Summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# Installer Validation Summary')
    $lines.Add('')
    $lines.Add("- Generated: $($Summary.generatedAtUtc)")
    $lines.Add("- Machine: $($Summary.machineName)")
    $lines.Add("- Elevated: $($Summary.isAdministrator)")
    $lines.Add("- Success: $($Summary.success)")
    $lines.Add('')

    foreach ($scenario in $Summary.scenarios) {
        $lines.Add("## $($scenario.name)")
        $lines.Add('')
        $lines.Add("- Status: $($scenario.status)")
        $lines.Add("- Message: $($scenario.message)")
        $lines.Add("- Baseline install exit code: $($scenario.baselineInstallExitCode)")
        $lines.Add("- Install exit code: $($scenario.installExitCode)")
        $lines.Add("- Uninstall exit code: $($scenario.uninstallExitCode)")
        $lines.Add("- Source MSI version: $($scenario.sourceMsiVersion)")
        $lines.Add("- Target MSI version: $($scenario.targetMsiVersion)")
        $lines.Add("- Service after install: $($scenario.serviceStatusAfterInstall)")
        $lines.Add("- Service removed after uninstall: $($scenario.serviceRemovedAfterUninstall)")
        $lines.Add("- Health status: $($scenario.healthStatus)")
        $lines.Add("- Readiness status: $($scenario.readinessStatus)")
        $lines.Add("- Diagnostics status: $($scenario.diagnosticsStatus)")
        if ($scenario.upgradePreservation) {
            $lines.Add("- Upgrade preserved API key: $($scenario.upgradePreservation.apiKey)")
            $lines.Add("- Upgrade preserved certificate password: $($scenario.upgradePreservation.certificatePassword)")
            $lines.Add("- Upgrade preserved certificate path: $($scenario.upgradePreservation.certificatePath)")
            $lines.Add("- Upgrade preserved handoff secrets: $($scenario.upgradePreservation.handoffSecrets)")
        }
        $lines.Add('')
    }

    $lines | Set-Content -LiteralPath $markdownPath -Encoding UTF8

    return [pscustomobject]@{
        jsonPath = $jsonPath
        markdownPath = $markdownPath
    }
}

$summary = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    machineName = $env:COMPUTERNAME
    isAdministrator = Test-IsAdministrator
    success = $false
    scenarios = New-Object System.Collections.Generic.List[object]
}

foreach ($scenarioName in $ScenarioNames) {
    $scenarioOutputDir = Join-Path $OutputDir $scenarioName
    New-Item -ItemType Directory -Path $scenarioOutputDir -Force | Out-Null

    $scenarioStartTimeUtc = [DateTime]::UtcNow
    $scenario = [ordered]@{
        name = $scenarioName
        startedAtUtc = $scenarioStartTimeUtc.ToString('o')
        finishedAtUtc = $null
        status = 'failed'
        message = ''
        sourceMsiPath = $PreviousMsiPath
        sourceMsiVersion = ''
        targetMsiPath = $MsiPath
        targetMsiVersion = ''
        baselineInstallExitCode = $null
        installExitCode = $null
        uninstallExitCode = $null
        serviceStatusAfterInstall = 'Absent'
        serviceRemovedAfterUninstall = $null
        healthStatus = $null
        readinessStatus = $null
        diagnosticsStatus = $null
        appSettings = $null
        handoff = $null
        diagnostics = $null
        baselineDiagnostics = $null
        upgradePreservation = $null
        artifacts = [ordered]@{
            installLog = Join-Path $scenarioOutputDir 'install.log'
            uninstallLog = Join-Path $scenarioOutputDir 'uninstall.log'
            baselineInstallLog = Join-Path $scenarioOutputDir 'baseline-install.log'
            bootstrapLog = $null
            bootstrapFailure = $null
            bootstrapPreflightLog = $null
            bootstrapPreflightFailure = $null
            startupFailure = $null
        }
    }

    $installAttempted = $false

    try {
        if ($supportedScenarios -notcontains $scenarioName) {
            throw "Unsupported scenario '$scenarioName'. Supported scenarios: $($supportedScenarios -join ', ')."
        }

        if (-not (Test-Path -LiteralPath $MsiPath -PathType Leaf)) {
            throw "MSI not found at '$MsiPath'. Build the installer before running installer validation."
        }

        if ($scenarioName -eq 'AdjacentUpgrade') {
            if ([string]::IsNullOrWhiteSpace($PreviousMsiPath)) {
                throw 'AdjacentUpgrade requires -PreviousMsiPath to point to a baseline MSI.'
            }

            if (-not (Test-Path -LiteralPath $PreviousMsiPath -PathType Leaf)) {
                throw "Baseline MSI not found at '$PreviousMsiPath'."
            }
        }

        if (-not $summary.isAdministrator) {
            throw 'Installer validation requires an elevated PowerShell session.'
        }

        $scenario.targetMsiVersion = Get-MsiProductVersion -PackagePath $MsiPath

        if ($scenarioName -eq 'AdjacentUpgrade') {
            $scenario.sourceMsiPath = $PreviousMsiPath
            $scenario.sourceMsiVersion = Get-MsiProductVersion -PackagePath $PreviousMsiPath

            if (-not [string]::IsNullOrWhiteSpace($scenario.sourceMsiVersion) -and -not [string]::IsNullOrWhiteSpace($scenario.targetMsiVersion) -and $scenario.sourceMsiVersion -eq $scenario.targetMsiVersion) {
                throw "AdjacentUpgrade requires different MSI product versions, but both packages report '$($scenario.targetMsiVersion)'."
            }
        }

        if ($ForceCleanMachine.IsPresent) {
            Ensure-CleanMachine -ScenarioOutputDir $scenarioOutputDir -ScenarioName $scenarioName
        }

        Assert-CleanMachine

        $installedAppSettingsPath = Join-Path $InstallDirectory 'appsettings.json'

        switch ($scenarioName) {
            'FreshSilentInstall' {
                $installAttempted = $true
                $scenario.installExitCode = Invoke-Msiexec -Mode install -PackagePath $MsiPath -LogPath $scenario.artifacts.installLog
                if ($scenario.installExitCode -ne 0) {
                    throw "msiexec install returned exit code $($scenario.installExitCode)."
                }

                if (-not (Wait-ForServiceStatus -Name $ServiceName -DesiredStatus Running -TimeoutSeconds $ServiceStartTimeoutSeconds)) {
                    throw "Service '$ServiceName' did not reach Running within $ServiceStartTimeoutSeconds seconds."
                }

                $snapshot = Get-InstalledAgentSnapshot -InstalledAppSettingsPath $installedAppSettingsPath
                $scenario.serviceStatusAfterInstall = Get-ServiceState -Name $ServiceName
                $scenario.appSettings = $snapshot.publicAppSettings
                $scenario.handoff = $snapshot.publicHandoff
                $scenario.diagnostics = $snapshot.publicDiagnostics
                $scenario.healthStatus = $snapshot.healthStatus
                $scenario.readinessStatus = $snapshot.readinessStatus
                $scenario.diagnosticsStatus = $snapshot.diagnosticsStatus
                $scenario.message = 'Fresh silent install validation passed.'
            }

            'AdjacentUpgrade' {
                $installAttempted = $true
                $scenario.baselineInstallExitCode = Invoke-Msiexec -Mode install -PackagePath $PreviousMsiPath -LogPath $scenario.artifacts.baselineInstallLog
                if ($scenario.baselineInstallExitCode -ne 0) {
                    throw "Baseline msiexec install returned exit code $($scenario.baselineInstallExitCode)."
                }

                if (-not (Wait-ForServiceStatus -Name $ServiceName -DesiredStatus Running -TimeoutSeconds $ServiceStartTimeoutSeconds)) {
                    throw "Service '$ServiceName' did not reach Running after baseline install within $ServiceStartTimeoutSeconds seconds."
                }

                $baselineSnapshot = Get-InstalledAgentSnapshot -InstalledAppSettingsPath $installedAppSettingsPath
                $scenario.baselineDiagnostics = $baselineSnapshot.publicDiagnostics

                $scenario.installExitCode = Invoke-Msiexec -Mode install -PackagePath $MsiPath -LogPath $scenario.artifacts.installLog
                if ($scenario.installExitCode -ne 0) {
                    throw "Upgrade msiexec install returned exit code $($scenario.installExitCode)."
                }

                if (-not (Wait-ForServiceStatus -Name $ServiceName -DesiredStatus Running -TimeoutSeconds $ServiceStartTimeoutSeconds)) {
                    throw "Service '$ServiceName' did not reach Running after upgrade within $ServiceStartTimeoutSeconds seconds."
                }

                $postUpgradeSnapshot = Get-InstalledAgentSnapshot -InstalledAppSettingsPath $installedAppSettingsPath -ExpectedApiKey $baselineSnapshot.internal.probeApiKey
                $scenario.serviceStatusAfterInstall = Get-ServiceState -Name $ServiceName
                $scenario.appSettings = $postUpgradeSnapshot.publicAppSettings
                $scenario.handoff = $postUpgradeSnapshot.publicHandoff
                $scenario.diagnostics = $postUpgradeSnapshot.publicDiagnostics
                $scenario.healthStatus = $postUpgradeSnapshot.healthStatus
                $scenario.readinessStatus = $postUpgradeSnapshot.readinessStatus
                $scenario.diagnosticsStatus = $postUpgradeSnapshot.diagnosticsStatus

                $apiKeyPreserved = $baselineSnapshot.internal.appSettingsApiKeyHash -eq $postUpgradeSnapshot.internal.appSettingsApiKeyHash
                $certificatePasswordPreserved = $baselineSnapshot.internal.appSettingsCertificatePasswordHash -eq $postUpgradeSnapshot.internal.appSettingsCertificatePasswordHash
                $certificatePathPreserved = $baselineSnapshot.publicAppSettings.certificatePath -eq $postUpgradeSnapshot.publicAppSettings.certificatePath
                $handoffSecretsPreserved = $baselineSnapshot.internal.handoffApiKeyHash -eq $postUpgradeSnapshot.internal.handoffApiKeyHash -and $baselineSnapshot.internal.handoffCertificatePasswordHash -eq $postUpgradeSnapshot.internal.handoffCertificatePasswordHash

                $scenario.upgradePreservation = [pscustomobject]@{
                    apiKey = $apiKeyPreserved
                    certificatePassword = $certificatePasswordPreserved
                    certificatePath = $certificatePathPreserved
                    handoffSecrets = $handoffSecretsPreserved
                }

                if (-not $apiKeyPreserved) {
                    throw 'Upgrade changed SecurityOptions.ApiKey unexpectedly.'
                }

                if (-not $certificatePasswordPreserved) {
                    throw 'Upgrade changed SecurityOptions.HttpsCertificatePassword unexpectedly.'
                }

                if (-not $certificatePathPreserved) {
                    throw 'Upgrade changed SecurityOptions.HttpsCertificatePath unexpectedly.'
                }

                if (-not $handoffSecretsPreserved) {
                    throw 'Upgrade changed bootstrap secret handoff contents unexpectedly.'
                }

                $scenario.message = 'Adjacent upgrade validation passed.'
            }
        }

        $scenario.status = 'passed'
    }
    catch {
        $scenario.status = 'failed'
        $scenario.message = $_.Exception.Message
    }
    finally {
        $scenario.artifacts.bootstrapLog = Copy-ArtifactIfFresh -SourcePath (Join-Path $diagnosticsRoot 'bootstrap.log') -DestinationDirectory $scenarioOutputDir -ScenarioStartTimeUtc $scenarioStartTimeUtc
        $scenario.artifacts.bootstrapFailure = Copy-ArtifactIfFresh -SourcePath (Join-Path $diagnosticsRoot 'bootstrap-failure.json') -DestinationDirectory $scenarioOutputDir -ScenarioStartTimeUtc $scenarioStartTimeUtc
        $scenario.artifacts.bootstrapPreflightLog = Copy-ArtifactIfFresh -SourcePath (Join-Path $diagnosticsRoot 'bootstrap-preflight.log') -DestinationDirectory $scenarioOutputDir -ScenarioStartTimeUtc $scenarioStartTimeUtc
        $scenario.artifacts.bootstrapPreflightFailure = Copy-ArtifactIfFresh -SourcePath (Join-Path $diagnosticsRoot 'bootstrap-preflight-failure.json') -DestinationDirectory $scenarioOutputDir -ScenarioStartTimeUtc $scenarioStartTimeUtc
        $scenario.artifacts.startupFailure = Copy-ArtifactIfFresh -SourcePath (Join-Path $diagnosticsRoot 'startup-failure.json') -DestinationDirectory $scenarioOutputDir -ScenarioStartTimeUtc $scenarioStartTimeUtc

        if ($installAttempted -and -not $KeepInstalled.IsPresent) {
            try {
                $scenario.uninstallExitCode = Invoke-Msiexec -Mode uninstall -PackagePath $MsiPath -LogPath $scenario.artifacts.uninstallLog
                if ($scenario.uninstallExitCode -eq 0) {
                    $scenario.serviceRemovedAfterUninstall = Wait-ForServiceStatus -Name $ServiceName -DesiredStatus Absent -TimeoutSeconds 60
                    if (-not $scenario.serviceRemovedAfterUninstall) {
                        throw "Service '$ServiceName' still exists after uninstall."
                    }
                }
                else {
                    throw "msiexec uninstall returned exit code $($scenario.uninstallExitCode)."
                }
            }
            catch {
                $scenario.serviceRemovedAfterUninstall = $false
                if ($scenario.status -eq 'passed') {
                    $scenario.status = 'failed'
                    $scenario.message = $_.Exception.Message
                }
                else {
                    $scenario.message = "$($scenario.message) Uninstall cleanup also failed: $($_.Exception.Message)"
                }
            }
        }

        $scenario.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        $summary.scenarios.Add([pscustomobject]$scenario)

        Write-Host "Installer scenario '$($scenario.name)' completed with status '$($scenario.status)': $($scenario.message)"
        if ($scenario.installExitCode -ne $null) {
            Write-Host "Installer scenario '$($scenario.name)' install exit code: $($scenario.installExitCode)"
        }
        if ($scenario.uninstallExitCode -ne $null) {
            Write-Host "Installer scenario '$($scenario.name)' uninstall exit code: $($scenario.uninstallExitCode)"
        }
    }
}

$summary.success = @($summary.scenarios | Where-Object status -ne 'passed').Count -eq 0
$summaryPaths = Write-SummaryArtifacts -Summary $summary -DestinationDirectory $OutputDir

Write-Host "Installer validation summary JSON: $($summaryPaths.jsonPath)"
Write-Host "Installer validation summary Markdown: $($summaryPaths.markdownPath)"

if (-not $summary.success -and $FailOnScenarioFailure.IsPresent) {
    throw 'Installer validation matrix reported one or more failed scenarios. See summary artifacts for details.'
}

return $summary