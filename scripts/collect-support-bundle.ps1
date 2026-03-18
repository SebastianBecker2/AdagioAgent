param(
    [string]$ApiBaseUrl = 'https://127.0.0.1:5443/api/v1',
    [string]$ApiKey = $env:ADAGIO_AGENT_API_KEY,
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\support-bundles'),
    [string]$ExtensionOutputPath,
    [int]$EventLogEntries = 200,
    [switch]$Offline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Ensure-Directory([string]$path) {
    if (-not (Test-Path $path -PathType Container)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
}

function Save-Json([string]$path, [object]$value) {
    $json = $value | ConvertTo-Json -Depth 20
    Set-Content -Path $path -Value $json -Encoding UTF8
}

function Mask-SensitiveData([object]$value) {
    if ($null -eq $value) {
        return $null
    }

    if ($value -is [System.Collections.IDictionary]) {
        $copy = @{}
        foreach ($entry in $value.GetEnumerator()) {
            $key = [string]$entry.Key
            if ($key -match '(?i)(apikey|password|secret|token|certificate)') {
                $copy[$key] = '[REDACTED]'
            } else {
                $copy[$key] = Mask-SensitiveData $entry.Value
            }
        }

        return $copy
    }

    if ($value -is [System.Collections.IEnumerable] -and -not ($value -is [string])) {
        $list = New-Object System.Collections.Generic.List[object]
        foreach ($item in $value) {
            $list.Add((Mask-SensitiveData $item))
        }

        return $list
    }

    return $value
}

function Invoke-AgentGet([string]$relativePath, [string]$destinationPath, [hashtable]$headers) {
    try {
        $uri = "$($ApiBaseUrl.TrimEnd('/'))/$($relativePath.TrimStart('/'))"
        $response = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get -ErrorAction Stop
        $sanitized = Mask-SensitiveData $response
        Save-Json -Path $destinationPath -Value $sanitized
        return $true
    } catch {
        Set-Content -Path $destinationPath -Value "Request failed: $($_.Exception.Message)" -Encoding UTF8
        return $false
    }
}

function Add-TextFile([string]$path, [string]$content) {
    Set-Content -Path $path -Value $content -Encoding UTF8
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bundleDir = Join-Path $OutputRoot "support-bundle-$timestamp"

Ensure-Directory $OutputRoot
Ensure-Directory $bundleDir

$manifest = [ordered]@{
    GeneratedAtUtc = [DateTimeOffset]::UtcNow
    BundleDirectory = (Resolve-Path $bundleDir).Path
    ApiBaseUrl = $ApiBaseUrl
    OfflineMode = [bool]$Offline
    ArtifactSchema = [ordered]@{
        RequiredArtifacts = @(
            'manifest.json',
            'machine-info.json',
            'service-status.json',
            'application-events.json'
        )
        OptionalArtifacts = @(
            'health.json',
            'ready.json',
            'diagnostics-status.json',
            'diagnostics-export-metadata.json',
            'offline-note.txt',
            'extension-output-metadata.json'
        )
    }
    IncludedFiles = @()
    Notes = @()
}

$machineInfo = [ordered]@{
    ComputerName = $env:COMPUTERNAME
    UserName = $env:USERNAME
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    OsVersion = [Environment]::OSVersion.VersionString
    DotnetInfo = (& dotnet --info) -join "`n"
}
$machineInfoPath = Join-Path $bundleDir 'machine-info.json'
Save-Json -Path $machineInfoPath -Value $machineInfo
$manifest.IncludedFiles += 'machine-info.json'

$serviceInfoPath = Join-Path $bundleDir 'service-status.json'
try {
    $service = Get-Service -Name 'AdagioMachineAgent' -ErrorAction Stop
    Save-Json -Path $serviceInfoPath -Value ([ordered]@{
        Name = $service.Name
        DisplayName = $service.DisplayName
        Status = [string]$service.Status
        StartType = [string]$service.StartType
    })
} catch {
    Add-TextFile -path $serviceInfoPath -content "Service status unavailable: $($_.Exception.Message)"
    $manifest.Notes += 'AdagioMachineAgent service not found or not accessible.'
}
$manifest.IncludedFiles += 'service-status.json'

$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $headers['X-API-Key'] = $ApiKey
} elseif (-not $Offline) {
    $manifest.Notes += 'No API key provided. Endpoint calls may fail if API key auth is enabled.'
}

if (-not $Offline) {
    $endpoints = @(
        @{ Path = 'health'; File = 'health.json' },
        @{ Path = 'ready'; File = 'ready.json' },
        @{ Path = 'diagnostics/status'; File = 'diagnostics-status.json' },
        @{ Path = 'diagnostics/export-metadata'; File = 'diagnostics-export-metadata.json' }
    )

    foreach ($endpoint in $endpoints) {
        $targetPath = Join-Path $bundleDir $endpoint.File
        $ok = Invoke-AgentGet -relativePath $endpoint.Path -destinationPath $targetPath -headers $headers
        $manifest.IncludedFiles += $endpoint.File
        if (-not $ok) {
            $manifest.Notes += "Endpoint collection failed for $($endpoint.Path)."
        }
    }
} else {
    $offlineNotePath = Join-Path $bundleDir 'offline-note.txt'
    Add-TextFile -path $offlineNotePath -content 'Offline mode enabled. API endpoint snapshots were skipped.'
    $manifest.IncludedFiles += 'offline-note.txt'
}

if (-not [string]::IsNullOrWhiteSpace($ExtensionOutputPath)) {
    $extensionOutputMetadataPath = Join-Path $bundleDir 'extension-output-metadata.json'
    try {
        $resolvedOutputPath = Resolve-Path -LiteralPath $ExtensionOutputPath -ErrorAction Stop
        Save-Json -Path $extensionOutputMetadataPath -Value ([ordered]@{
            ProvidedPath = $ExtensionOutputPath
            ResolvedPath = $resolvedOutputPath.Path
            Exists = $true
            LastWriteTimeUtc = (Get-Item -LiteralPath $resolvedOutputPath.Path).LastWriteTimeUtc
        })
    } catch {
        Save-Json -Path $extensionOutputMetadataPath -Value ([ordered]@{
            ProvidedPath = $ExtensionOutputPath
            Exists = $false
            Error = $_.Exception.Message
        })
        $manifest.Notes += 'Extension output path metadata was requested but the path could not be resolved.'
    }

    $manifest.IncludedFiles += 'extension-output-metadata.json'
}

$eventLogPath = Join-Path $bundleDir 'application-events.json'
try {
    $events = Get-WinEvent -LogName Application -MaxEvents $EventLogEntries -ErrorAction Stop |
        Where-Object {
            $_.ProviderName -like '*Adagio*' -or $_.Message -like '*Adagio*' -or $_.Message -like '*AdagioMachineAgent*'
        } |
        Select-Object TimeCreated, Id, LevelDisplayName, ProviderName, Message

    Save-Json -Path $eventLogPath -Value $events
} catch {
    Add-TextFile -path $eventLogPath -content "Application event log collection failed: $($_.Exception.Message)"
    $manifest.Notes += 'Windows Application event log collection failed.'
}
$manifest.IncludedFiles += 'application-events.json'

$manifestPath = Join-Path $bundleDir 'manifest.json'
Save-Json -Path $manifestPath -Value $manifest

Write-Host "Support bundle created: $bundleDir"
