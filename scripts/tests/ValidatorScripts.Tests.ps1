$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$checkEvidenceRefsScript = Join-Path $repoRoot 'scripts\check-signoff-evidence-references.ps1'
$checkIndexReferenceScript = Join-Path $repoRoot 'scripts\check-signoff-evidence-index-reference.ps1'
$checkIndexContentScript = Join-Path $repoRoot 'scripts\check-evidence-index-content.ps1'
$tagReadinessSummaryScript = Join-Path $repoRoot 'scripts\generate-release-ops-tag-readiness-summary.ps1'

function New-TestFile {
    param(
        [string]$RelativePath,
        [string]$Content
    )

    $fullPath = Join-Path $repoRoot $RelativePath
    $parent = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    Set-Content -LiteralPath $fullPath -Value $Content -Encoding UTF8
    $script:createdFiles += $fullPath
    return $fullPath
}

function New-CiStatusReportFixture {
    param(
        [string]$RelativePath,
        [string]$OverallStatus = 'pass'
    )

    $content = @{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
        diagnosticsRoot = 'artifacts/release-ops-dryrun-diagnostics'
        overallStatus = $OverallStatus
        qualityGates = @{
            indexFresh = @{ passed = $true; message = 'ok' }
            trendGate = @{ passed = ($OverallStatus -ne 'hold' -and $OverallStatus -ne 'escalate'); level = $OverallStatus; message = 'trend' }
        }
        summary = @{ totalEntries = 5; successCount = 5; failureCount = 0; recentEntryCount = 5 }
    } | ConvertTo-Json -Depth 6

    New-TestFile -RelativePath $RelativePath -Content $content | Out-Null
}

function New-EvidenceFixture {
    param(
        [string]$Version,
        [string]$DateStamp,
        [switch]$IncludeIndex,
        [switch]$IncludeIndexEntries,
        [switch]$UsePlaceholderIndexEntry,
        [switch]$BreakIndexCrossLink,
        [switch]$UsePlaceholderSupportPath
    )

    $supportPath = "docs/release-ops/evidence/support-bundles/v$Version-bundle.json"
    $tracePath = "docs/release-ops/evidence/correlation-traces/v$Version-trace.md"
    $rollbackPath = "docs/release-ops/evidence/rollback/v$Version-rollback.md"
    $upgradePath = "docs/release-ops/evidence/upgrade-validation/v$Version-upgrade.md"
    $indexPath = "docs/release-ops/evidence/indexes/v$Version-$DateStamp-evidence.md"
    $signoffPath = "docs/release-ops/signoffs/v$Version-$DateStamp.md"

    New-TestFile -RelativePath $supportPath -Content '{"ok": true}' | Out-Null
    New-TestFile -RelativePath $tracePath -Content 'trace' | Out-Null
    New-TestFile -RelativePath $rollbackPath -Content 'rollback' | Out-Null
    New-TestFile -RelativePath $upgradePath -Content 'upgrade' | Out-Null

    $signoffIndexValue = $indexPath
    $supportSignoffValue = if ($UsePlaceholderSupportPath) { '<artifact>' } else { $supportPath }
    $signoffContent = @(
        '# Release Ops Sign-Off Record',
        '',
        "- Evidence index path: $signoffIndexValue",
        "- Support bundle evidence path: $supportSignoffValue",
        "- Correlation trace evidence path: $tracePath",
        "- Rollback rehearsal evidence path: $rollbackPath",
        "- Upgrade validation evidence path: $upgradePath"
    ) -join "`n"

    New-TestFile -RelativePath $signoffPath -Content $signoffContent | Out-Null

    if ($IncludeIndex) {
        $signoffReference = if ($BreakIndexCrossLink) {
            "docs/release-ops/signoffs/v$Version-00000000.md"
        } else {
            $signoffPath
        }

        if ($IncludeIndexEntries) {
            $supportIndexValue = if ($UsePlaceholderIndexEntry) { '<artifact>' } else { $supportPath }
            $indexContent = @(
                "# Evidence Index For v$Version",
                '',
                "- SignOffRecord: $signoffReference",
                '',
                '## Evidence Paths',
                '',
                "- Support bundle: $supportIndexValue",
                "- Correlation trace: $tracePath",
                "- Rollback rehearsal: $rollbackPath",
                "- Upgrade validation: $upgradePath"
            ) -join "`n"
        }
        else {
            $indexContent = @(
                "# Evidence Index For v$Version",
                '',
                "- SignOffRecord: $signoffReference"
            ) -join "`n"
        }

        New-TestFile -RelativePath $indexPath -Content $indexContent | Out-Null
    }
}

Describe 'Release evidence validators' {
    BeforeEach {
        $script:createdFiles = @()
        $script:originalTag = $env:APPVEYOR_REPO_TAG
        $script:originalTagName = $env:APPVEYOR_REPO_TAG_NAME

        $script:testVersion = "9.9.$(Get-Random -Minimum 100 -Maximum 999)"
        $script:testDateStamp = '20990101'
        $env:APPVEYOR_REPO_TAG = 'true'
        $env:APPVEYOR_REPO_TAG_NAME = "v$script:testVersion"
    }

    AfterEach {
        if ($null -eq $script:originalTag) {
            Remove-Item Env:APPVEYOR_REPO_TAG -ErrorAction SilentlyContinue
        }
        else {
            $env:APPVEYOR_REPO_TAG = $script:originalTag
        }

        if ($null -eq $script:originalTagName) {
            Remove-Item Env:APPVEYOR_REPO_TAG_NAME -ErrorAction SilentlyContinue
        }
        else {
            $env:APPVEYOR_REPO_TAG_NAME = $script:originalTagName
        }

        foreach ($file in $script:createdFiles) {
            if (Test-Path -LiteralPath $file -PathType Leaf) {
                Remove-Item -LiteralPath $file -Force
            }
            elseif (Test-Path -LiteralPath $file -PathType Container) {
                Remove-Item -LiteralPath $file -Recurse -Force
            }
        }
    }

    It 'check-signoff-evidence-references passes with valid repo evidence paths' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp | Out-Null

        { & $checkEvidenceRefsScript } | Should Not Throw
    }

    It 'check-signoff-evidence-references fails when sign-off evidence value is placeholder' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -UsePlaceholderSupportPath | Out-Null

        { & $checkEvidenceRefsScript } | Should Throw
    }

    It 'check-signoff-evidence-index-reference passes with valid cross-link' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex | Out-Null

        { & $checkIndexReferenceScript } | Should Not Throw
    }

    It 'check-signoff-evidence-index-reference fails when index cross-link does not match sign-off' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -BreakIndexCrossLink | Out-Null

        { & $checkIndexReferenceScript } | Should Throw
    }

    It 'check-evidence-index-content passes with required entries and concrete paths' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        { & $checkIndexContentScript } | Should Not Throw
    }

    It 'check-evidence-index-content fails when an index entry uses placeholder value' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries -UsePlaceholderIndexEntry | Out-Null

        { & $checkIndexContentScript } | Should Throw
    }

    It 'generate-release-ops-tag-readiness-summary reports ready when validators pass and CI status is pass' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null
        New-CiStatusReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/ci-status-pass.json' -OverallStatus 'pass'

        $outputDir = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\output-pass'
        if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
            New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
            $script:createdFiles += $outputDir
        }

        { & $tagReadinessSummaryScript -TagName "v$script:testVersion" -CiStatusReportPath (Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\ci-status-pass.json') -OutputDir $outputDir } | Should Not Throw

        $summaryPath = Join-Path $outputDir 'release-ops-tag-readiness-summary.json'
        (Test-Path -LiteralPath $summaryPath -PathType Leaf) | Should Be $true

        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $summary.readinessVerdict | Should Be 'ready'
        $summary.validatorSummary.failed | Should Be 0
    }

    It 'generate-release-ops-tag-readiness-summary reports hold when a validator fails' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries -UsePlaceholderSupportPath | Out-Null
        New-CiStatusReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/ci-status-pass-2.json' -OverallStatus 'pass'

        $outputDir = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\output-hold'
        if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
            New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
            $script:createdFiles += $outputDir
        }

        { & $tagReadinessSummaryScript -TagName "v$script:testVersion" -CiStatusReportPath (Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\ci-status-pass-2.json') -OutputDir $outputDir } | Should Not Throw

        $summaryPath = Join-Path $outputDir 'release-ops-tag-readiness-summary.json'
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        $summary.readinessVerdict | Should Be 'hold'
        $summary.validatorSummary.failed | Should BeGreaterThan 0
    }
}
