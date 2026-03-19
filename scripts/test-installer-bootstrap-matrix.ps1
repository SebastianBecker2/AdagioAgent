[CmdletBinding()]
param(
    [string]$MsiPath = "installer\bin\x64\Release\AdagioMachineAgentSetup.msi",
    [string]$OutputDir = "artifacts\installer-validation",
    [string[]]$ScenarioNames = @("FreshSilentInstall"),
    [string]$InstallDirectory = "${env:ProgramFiles}\AdagioMachineAgent",
    [string]$ServiceName = "AdagioMachineAgent",
    [string]$BaseUrl = "https://127.0.0.1:5443",
    [int]$ServiceStartTimeoutSeconds = 120,
    [switch]$KeepInstalled,
    [switch]$FailOnScenarioFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not [System.IO.Path]::IsPathRooted($MsiPath)) {
    $MsiPath = Join-Path $repoRoot $MsiPath
}

if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $repoRoot $OutputDir
}

$supportedScenarios = @('FreshSilentInstall')
$diagnosticsRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'AdagioMachineAgent'
$handoffPath = Join-Path $diagnosticsRoot 'bootstrap-secrets.json'

function Test-IsAdministrator {
    $currentIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($currentIdentity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Initialize-CertificateBypass {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
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

function Invoke-AgentRequest {
    param(
        [string]$RelativePath,
        [string]$ApiKey,
        [string]$ApiKeyHeaderName = 'X-API-Key'
    )

    Initialize-CertificateBypass

    $uri = '{0}{1}' -f $BaseUrl.TrimEnd('/'), $RelativePath
    return Invoke-RestMethod -Uri $uri -Headers @{ $ApiKeyHeaderName = $ApiKey } -Method Get -TimeoutSec 20 -UseBasicParsing
}

function Invoke-Msiexec {
    param(
        [ValidateSet('install', 'uninstall')]
        [string]$Mode,
        [string]$PackagePath,
        [string]$LogPath
    )

    $operation = if ($Mode -eq 'install') { '/i' } else { '/x' }
    $arguments = "$operation `"$PackagePath`" /qn /norestart /l*v `"$LogPath`""
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

function Write-SummaryArtifacts {
    param(
        [pscustomobject]$Summary,
        [string]$DestinationDirectory
    )

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null

    $jsonPath = Join-Path $DestinationDirectory 'installer-validation-summary.json'
    $markdownPath = Join-Path $DestinationDirectory 'installer-validation-summary.md'

    $Summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

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
        $lines.Add("- Install exit code: $($scenario.installExitCode)")
        $lines.Add("- Uninstall exit code: $($scenario.uninstallExitCode)")
        $lines.Add("- Service after install: $($scenario.serviceStatusAfterInstall)")
        $lines.Add("- Service removed after uninstall: $($scenario.serviceRemovedAfterUninstall)")
        $lines.Add("- Health status: $($scenario.healthStatus)")
        $lines.Add("- Readiness status: $($scenario.readinessStatus)")
        $lines.Add("- Diagnostics status: $($scenario.diagnosticsStatus)")
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
        artifacts = [ordered]@{
            installLog = Join-Path $scenarioOutputDir 'install.log'
            uninstallLog = Join-Path $scenarioOutputDir 'uninstall.log'
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

        if (-not $summary.isAdministrator) {
            throw 'Installer validation requires an elevated PowerShell session.'
        }

        if ((Get-ServiceState -Name $ServiceName) -ne 'Absent') {
            throw "Service '$ServiceName' is already installed. Refusing to modify a non-clean machine."
        }

        if (Test-Path -LiteralPath $InstallDirectory -PathType Container) {
            throw "Install directory '$InstallDirectory' already exists. Refusing to modify a non-clean machine."
        }

        $installAttempted = $true
        $scenario.installExitCode = Invoke-Msiexec -Mode install -PackagePath $MsiPath -LogPath $scenario.artifacts.installLog
        if ($scenario.installExitCode -ne 0) {
            throw "msiexec install returned exit code $($scenario.installExitCode)."
        }

        if (-not (Wait-ForServiceStatus -Name $ServiceName -DesiredStatus Running -TimeoutSeconds $ServiceStartTimeoutSeconds)) {
            throw "Service '$ServiceName' did not reach Running within $ServiceStartTimeoutSeconds seconds."
        }

        $scenario.serviceStatusAfterInstall = Get-ServiceState -Name $ServiceName

        $installedAppSettingsPath = Join-Path $InstallDirectory 'appsettings.json'
        if (-not (Test-Path -LiteralPath $installedAppSettingsPath -PathType Leaf)) {
            throw "Installed appsettings.json not found at '$installedAppSettingsPath'."
        }

        $appSettings = Get-Content -LiteralPath $installedAppSettingsPath -Raw | ConvertFrom-Json
        $securityOptions = $appSettings.SecurityOptions
        if (-not $securityOptions) {
            throw 'Installed appsettings.json is missing the SecurityOptions section.'
        }

        $apiKeyConfigured = -not [string]::IsNullOrWhiteSpace([string]$securityOptions.ApiKey) -and [string]$securityOptions.ApiKey -ne 'CHANGE_ME'
        $certificatePasswordConfigured = -not [string]::IsNullOrWhiteSpace([string]$securityOptions.HttpsCertificatePassword) -and [string]$securityOptions.HttpsCertificatePassword -ne 'CHANGE_ME_CERT_PASSWORD'
        $certificatePath = [string]$securityOptions.HttpsCertificatePath
        $certificateExists = -not [string]::IsNullOrWhiteSpace($certificatePath) -and (Test-Path -LiteralPath $certificatePath -PathType Leaf)

        $scenario.appSettings = [pscustomobject]@{
            exists = $true
            path = $installedAppSettingsPath
            apiKeyConfigured = $apiKeyConfigured
            certificatePasswordConfigured = $certificatePasswordConfigured
            certificatePath = $certificatePath
            certificateExists = $certificateExists
        }

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
        $handoffAcl = Get-Acl -LiteralPath $handoffPath
        $handoffSidValues = @($handoffAcl.Access | ForEach-Object { Convert-IdentityToSid -IdentityReference $_.IdentityReference } | Sort-Object -Unique)
        $expectedHandoffSids = @('S-1-5-18', 'S-1-5-32-544')

        $scenario.handoff = [pscustomobject]@{
            exists = $true
            path = $handoffPath
            apiKeyPresent = -not [string]::IsNullOrWhiteSpace([string]$handoff.apiKey)
            certificatePasswordPresent = -not [string]::IsNullOrWhiteSpace([string]$handoff.httpsCertificatePassword)
            certificatePathMatchesAppSettings = [string]$handoff.httpsCertificatePath -eq $certificatePath
            aclProtected = $handoffAcl.AreAccessRulesProtected
            allowedSidValues = $handoffSidValues
        }

        if (-not $scenario.handoff.apiKeyPresent) {
            throw 'Bootstrap secret handoff file did not contain an API key.'
        }

        if (-not $scenario.handoff.certificatePasswordPresent) {
            throw 'Bootstrap secret handoff file did not contain a certificate password.'
        }

        if (-not $scenario.handoff.certificatePathMatchesAppSettings) {
            throw 'Bootstrap secret handoff certificate path does not match installed appsettings.json.'
        }

        if (-not $scenario.handoff.aclProtected) {
            throw 'Bootstrap secret handoff file ACL is not protected.'
        }

        if (@($handoffSidValues | Where-Object { $expectedHandoffSids -notcontains $_ }).Count -ne 0 -or @($expectedHandoffSids | Where-Object { $handoffSidValues -notcontains $_ }).Count -ne 0) {
            throw 'Bootstrap secret handoff file ACL does not match the expected SYSTEM and Administrators-only access.'
        }

        $apiKey = [string]$handoff.apiKey
        $health = Invoke-AgentRequest -RelativePath '/health' -ApiKey $apiKey
        $ready = Invoke-AgentRequest -RelativePath '/ready' -ApiKey $apiKey
        $diagnostics = Invoke-AgentRequest -RelativePath '/diagnostics/status' -ApiKey $apiKey
        $exportMetadata = Invoke-AgentRequest -RelativePath '/diagnostics/export-metadata' -ApiKey $apiKey

        $scenario.healthStatus = [string]$health.status
        $scenario.readinessStatus = [string]$ready.status
        $scenario.diagnosticsStatus = [string]$diagnostics.status
        $scenario.diagnostics = [pscustomobject]@{
            healthApiVersion = $health.apiVersion
            readyApiVersion = $ready.apiVersion
            diagnosticsApiVersion = $diagnostics.apiVersion
            exportMetadataApiKeyHeaderName = [string]$exportMetadata.apiKeyHeaderName
            exportMetadataHttpsRequired = [bool]$exportMetadata.httpsRequired
            exportMetadataApiKeyRequired = [bool]$exportMetadata.apiKeyRequired
            readinessIssueCount = @($ready.issues).Count
        }

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

        $scenario.status = 'passed'
        $scenario.message = 'Fresh silent install validation passed.'
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