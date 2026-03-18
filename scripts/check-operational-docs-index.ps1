Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$readmePath = Join-Path $repoRoot 'README.md'

if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf)) {
    throw 'README.md not found.'
}

$readmeContent = Get-Content -LiteralPath $readmePath -Raw

$requiredLinks = @(
    'docs/PILOT_RUNBOOK.md',
    'docs/ROLLBACK_CHECKLIST.md',
    'docs/UPGRADE_VALIDATION_CHECKLIST.md',
    'docs/SUPPORT_BUNDLE_SCHEMA.md',
    'docs/RELEASE_SUPPORT_QUICKSTART.md',
    'docs/OBSERVABILITY_FIELDS.md',
    'docs/DIAGNOSTICS_TROUBLESHOOTING.md'
)

$missing = New-Object System.Collections.Generic.List[string]

foreach ($relativePath in $requiredLinks) {
    if ($readmeContent -notmatch [regex]::Escape($relativePath)) {
        $missing.Add("README.md missing operational docs index link: $relativePath")
        continue
    }

    $target = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        $missing.Add("Linked operational doc does not exist: $relativePath")
    }
}

if ($missing.Count -gt 0) {
    Write-Host 'Operational docs index check failed:'
    foreach ($item in $missing) {
        Write-Host " - $item"
    }

    throw "Operational docs index check failed with $($missing.Count) issue(s)."
}

Write-Host 'Operational docs index check passed.'
