Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

$requirements = @(
    @{ File = 'docs/OBSERVABILITY_FIELDS.md'; MustContain = @('CorrelationId', 'DurationMs', 'error', 'detail', 'correlationId') },
    @{ File = 'SUPPORT.md'; MustContain = @('Severity Matrix', 'Correlation ID', 'SLA') },
    @{ File = 'docs/PILOT_RUNBOOK.md'; MustContain = @('Correlation ID', 'incident', 'timeline') },
    @{ File = 'docs/DIAGNOSTICS_TROUBLESHOOTING.md'; MustContain = @('Correlation ID') }
)

$missing = New-Object System.Collections.Generic.List[string]

foreach ($req in $requirements) {
    $filePath = Join-Path $repoRoot $req.File
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        $missing.Add("Missing required observability doc: $($req.File)")
        continue
    }

    $content = Get-Content -LiteralPath $filePath -Raw
    foreach ($needle in $req.MustContain) {
        if ($content -notmatch [regex]::Escape($needle)) {
            $missing.Add("$($req.File) does not mention required token: $needle")
        }
    }
}

if ($missing.Count -gt 0) {
    Write-Host 'Observability docs consistency check failed:'
    foreach ($item in $missing) {
        Write-Host " - $item"
    }

    throw "Observability docs consistency check failed with $($missing.Count) issue(s)."
}

Write-Host 'Observability docs consistency check passed.'
