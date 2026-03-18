Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-MarkdownLinks([string]$content) {
    $pattern = '\[[^\]]+\]\(([^\)]+)\)'
    $matches = [regex]::Matches($content, $pattern)
    foreach ($match in $matches) {
        $target = $match.Groups[1].Value.Trim()
        if ($target.Length -eq 0) {
            continue
        }

        if ($target.StartsWith('http://') -or $target.StartsWith('https://') -or $target.StartsWith('mailto:') -or $target.StartsWith('#')) {
            continue
        }

        $normalized = $target.Replace('%20', ' ')
        if ($normalized.Contains('#')) {
            $normalized = $normalized.Split('#')[0]
        }

        if ($normalized.Length -eq 0) {
            continue
        }

        [PSCustomObject]@{ Target = $normalized; Raw = $target }
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$filesToCheck = @(
    'README.md',
    'SUPPORT.md'
)

$missing = New-Object System.Collections.Generic.List[string]

foreach ($relativeFile in $filesToCheck) {
    $fullPath = Join-Path $repoRoot $relativeFile
    if (-not (Test-Path $fullPath -PathType Leaf)) {
        throw "Required file for link check not found: $relativeFile"
    }

    $content = Get-Content -Path $fullPath -Raw
    $links = Get-MarkdownLinks -content $content

    foreach ($link in $links) {
        $targetPath = Join-Path $repoRoot $link.Target
        if (-not (Test-Path $targetPath)) {
            $missing.Add("$relativeFile -> $($link.Raw)")
        }
    }
}

if ($missing.Count -gt 0) {
    Write-Host 'Missing markdown link targets detected:'
    foreach ($item in $missing) {
        Write-Host " - $item"
    }

    throw "Documentation link check failed with $($missing.Count) missing target(s)."
}

Write-Host 'Documentation link check passed for README.md and SUPPORT.md.'
