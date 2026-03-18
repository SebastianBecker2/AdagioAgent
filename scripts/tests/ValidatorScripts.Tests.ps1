$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$checkEvidenceRefsScript = Join-Path $repoRoot 'scripts\check-signoff-evidence-references.ps1'
$checkIndexReferenceScript = Join-Path $repoRoot 'scripts\check-signoff-evidence-index-reference.ps1'
$checkIndexContentScript = Join-Path $repoRoot 'scripts\check-evidence-index-content.ps1'
$tagReadinessSummaryScript = Join-Path $repoRoot 'scripts\generate-release-ops-tag-readiness-summary.ps1'
$tagReadinessHistoryScript = Join-Path $repoRoot 'scripts\update-release-ops-tag-readiness-history.ps1'
$promotionGateScript = Join-Path $repoRoot 'scripts\check-release-ops-promotion-gate.ps1'
$promotionGateTrendScript = Join-Path $repoRoot 'scripts\update-release-ops-promotion-gate-trend.ps1'
$closureManifestScript = Join-Path $repoRoot 'scripts\generate-release-ops-closure-package-manifest.ps1'
$closureManifestCheckScript = Join-Path $repoRoot 'scripts\check-release-ops-closure-package-manifest.ps1'
$closureManifestDriftScript = Join-Path $repoRoot 'scripts\check-release-ops-closure-package-drift.ps1'
$closureIntegrityReportScript = Join-Path $repoRoot 'scripts\generate-release-ops-closure-package-integrity-report.ps1'
$closureIntegrityHistoryScript = Join-Path $repoRoot 'scripts\update-release-ops-closure-package-integrity-history.ps1'
$closureIntegrityGateScript    = Join-Path $repoRoot 'scripts\check-release-ops-closure-package-integrity-gate.ps1'

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

function New-TagReadinessSummaryFixture {
    param(
        [string]$RelativePath,
        [string]$TagName,
        [string]$Verdict,
        [string]$DiagnosticsOverallStatus = 'pass',
        [int]$ValidatorFailed = 0,
        [DateTimeOffset]$GeneratedAtUtc = [DateTimeOffset]::UtcNow
    )

    $content = @{
        generatedAtUtc = $GeneratedAtUtc.ToString('u')
        tagName = $TagName
        readinessVerdict = $Verdict
        readinessMessage = 'fixture'
        validatorSummary = @{
            total = 3
            passed = 3 - $ValidatorFailed
            failed = $ValidatorFailed
            results = @()
        }
        diagnosticsQualityGate = @{
            available = $true
            overallStatus = $DiagnosticsOverallStatus
            trendLevel = $DiagnosticsOverallStatus
            indexFreshPassed = $true
            trendGatePassed = ($DiagnosticsOverallStatus -eq 'pass' -or $DiagnosticsOverallStatus -eq 'pass-with-note')
            message = 'fixture'
        }
    } | ConvertTo-Json -Depth 6

    New-TestFile -RelativePath $RelativePath -Content $content | Out-Null
}

function New-TagReadinessHistoryIndexFixture {
    param(
        [string]$RelativePath,
        [array]$Entries
    )

    $content = @{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u')
        readinessRoot = 'artifacts/release-ops-tag-readiness'
        retentionDays = 180
        maxEntries = 20
        removedStaleEntries = 0
        totalEntries = $Entries.Count
        verdictCounts = @{
            ready = @($Entries | Where-Object { $_.readinessVerdict -eq 'ready' }).Count
            'ready-with-note' = @($Entries | Where-Object { $_.readinessVerdict -eq 'ready-with-note' }).Count
            hold = @($Entries | Where-Object { $_.readinessVerdict -eq 'hold' }).Count
            unknown = @($Entries | Where-Object { @('ready','ready-with-note','hold') -notcontains $_.readinessVerdict }).Count
        }
        entries = $Entries
    } | ConvertTo-Json -Depth 8

    New-TestFile -RelativePath $RelativePath -Content $content | Out-Null
}

function New-PromotionGateReportFixture {
    param(
        [string]$RelativePath,
        [string]$Verdict,
        [bool]$GatePassed,
        [bool]$OverrideUsed,
        [DateTimeOffset]$GeneratedAtUtc = [DateTimeOffset]::UtcNow
    )

    $content = @{
        generatedAtUtc = $GeneratedAtUtc.ToString('u')
        historyIndexPath = 'artifacts/release-ops-tag-readiness/release-ops-tag-readiness-history-index.json'
        promotionVerdict = $Verdict
        gatePassed = $GatePassed
        decisionReason = 'fixture'
        thresholds = @{ requiredRecentReadyCount = 3; noHoldInRecentCount = 2 }
        directorOverride = @{ allowed = $true; used = $OverrideUsed; reference = if ($OverrideUsed) { 'DIR-1' } else { '' } }
        summary = @{ totalEntries = 3; latestEntries = @() }
    } | ConvertTo-Json -Depth 8

    New-TestFile -RelativePath $RelativePath -Content $content | Out-Null
}

function New-ClosureIntegrityHistoryIndexFixture {
    param(
        [string]$RelativePath,
        [object[]]$Entries = @()
    )

    $passCount    = @($Entries | Where-Object { $_.integrityVerdict -eq 'pass' }).Count
    $failCount    = @($Entries | Where-Object { $_.integrityVerdict -eq 'fail' }).Count
    $unknownCount = @($Entries | Where-Object { @('pass','fail') -notcontains $_.integrityVerdict }).Count

    $content = @{
        generatedAtUtc        = [DateTimeOffset]::UtcNow.ToString('o')
        totalEntries          = $Entries.Count
        verdictCounts         = @{ pass = $passCount; fail = $failCount; unknown = $unknownCount }
        uniqueManifestHashCount = 1
        entries               = $Entries
    } | ConvertTo-Json -Depth 8

    New-TestFile -RelativePath $RelativePath -Content $content | Out-Null
}

function New-ClosureIntegrityReportFixture {
    param(
        [string]$RelativePath,
        [string]$TagName,
        [string]$Verdict,
        [int]$IssueCount = 0,
        [string]$ManifestSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
        [DateTimeOffset]$GeneratedAtUtc = [DateTimeOffset]::UtcNow
    )

    $content = @{
        generatedAtUtc = $GeneratedAtUtc.ToString('o')
        tagName = $TagName
        integrityVerdict = $Verdict
        issueCount = $IssueCount
        issues = @()
        manifest = @{
            path = 'artifacts/release-ops-tag-readiness/release-ops-closure-package-manifest.json'
            sha256 = $ManifestSha
        }
        verifiedArtifactCount = 4
        verifiedArtifacts = @()
    } | ConvertTo-Json -Depth 8

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

    It 'update-release-ops-tag-readiness-history archives latest summary and writes history index' {
        $historyRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\history-a'
        if (-not (Test-Path -LiteralPath $historyRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $historyRoot -Force | Out-Null
            $script:createdFiles += $historyRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/history-a/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0 -GeneratedAtUtc ([DateTimeOffset]::UtcNow.AddMinutes(-1))
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/history-a/release-ops-tag-readiness-summary.md' -Content '# latest' | Out-Null

        { & $tagReadinessHistoryScript -ReadinessRoot $historyRoot -ArchiveLatest -MaxEntries 20 -RetentionDays 180 } | Should Not Throw

        $indexPath = Join-Path $historyRoot 'release-ops-tag-readiness-history-index.json'
        (Test-Path -LiteralPath $indexPath -PathType Leaf) | Should Be $true

        $archived = @(Get-ChildItem -LiteralPath $historyRoot -File -Filter 'release-ops-tag-readiness-summary-*.json' |
            Where-Object { $_.Name -ne 'release-ops-tag-readiness-summary.json' })
        $archived.Count | Should BeGreaterThan 0

        $index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
        $index.totalEntries | Should BeGreaterThan 0
        $index.verdictCounts.ready | Should Be 1
    }

    It 'update-release-ops-tag-readiness-history prunes stale archived summaries' {
        $historyRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\history-b'
        if (-not (Test-Path -LiteralPath $historyRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $historyRoot -Force | Out-Null
            $script:createdFiles += $historyRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/history-b/release-ops-tag-readiness-summary-20000101000000-vold.json' -TagName 'v0.0.1' -Verdict 'hold' -DiagnosticsOverallStatus 'hold' -ValidatorFailed 1 -GeneratedAtUtc ([DateTimeOffset]::UtcNow.AddYears(-10))
        $stalePath = Join-Path $historyRoot 'release-ops-tag-readiness-summary-20000101000000-vold.json'
        (Get-Item -LiteralPath $stalePath).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-400)

        { & $tagReadinessHistoryScript -ReadinessRoot $historyRoot -MaxEntries 20 -RetentionDays 180 } | Should Not Throw

        (Test-Path -LiteralPath $stalePath -PathType Leaf) | Should Be $false
    }

    It 'check-release-ops-promotion-gate passes when latest 3 verdicts are ready and no holds in latest 2' {
        $gateRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\promotion-pass'
        if (-not (Test-Path -LiteralPath $gateRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $gateRoot -Force | Out-Null
            $script:createdFiles += $gateRoot
        }

        $entries = @(
            [pscustomobject]@{ fileName = 'a.json'; generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u'); tagName = 'v1.0.3'; readinessVerdict = 'ready'; validatorFailed = 0; diagnosticsOverallStatus = 'pass' },
            [pscustomobject]@{ fileName = 'b.json'; generatedAtUtc = [DateTimeOffset]::UtcNow.AddDays(-1).ToString('u'); tagName = 'v1.0.2'; readinessVerdict = 'ready'; validatorFailed = 0; diagnosticsOverallStatus = 'pass' },
            [pscustomobject]@{ fileName = 'c.json'; generatedAtUtc = [DateTimeOffset]::UtcNow.AddDays(-2).ToString('u'); tagName = 'v1.0.1'; readinessVerdict = 'ready'; validatorFailed = 0; diagnosticsOverallStatus = 'pass' }
        )

        New-TagReadinessHistoryIndexFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/promotion-pass/release-ops-tag-readiness-history-index.json' -Entries $entries

        { & $promotionGateScript -ReadinessRoot $gateRoot -FailOnBlock } | Should Not Throw

        $reportPath = Join-Path $gateRoot 'release-ops-promotion-gate-report.json'
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $report.gatePassed | Should Be $true
        $report.promotionVerdict | Should Be 'pass'
    }

    It 'check-release-ops-promotion-gate requires director approval when recent verdicts include ready-with-note' {
        $gateRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\promotion-director'
        if (-not (Test-Path -LiteralPath $gateRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $gateRoot -Force | Out-Null
            $script:createdFiles += $gateRoot
        }

        $entries = @(
            [pscustomobject]@{ fileName = 'a.json'; generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('u'); tagName = 'v1.0.3'; readinessVerdict = 'ready-with-note'; validatorFailed = 0; diagnosticsOverallStatus = 'pass-with-note' },
            [pscustomobject]@{ fileName = 'b.json'; generatedAtUtc = [DateTimeOffset]::UtcNow.AddDays(-1).ToString('u'); tagName = 'v1.0.2'; readinessVerdict = 'ready'; validatorFailed = 0; diagnosticsOverallStatus = 'pass' },
            [pscustomobject]@{ fileName = 'c.json'; generatedAtUtc = [DateTimeOffset]::UtcNow.AddDays(-2).ToString('u'); tagName = 'v1.0.1'; readinessVerdict = 'ready'; validatorFailed = 0; diagnosticsOverallStatus = 'pass' }
        )

        New-TagReadinessHistoryIndexFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/promotion-director/release-ops-tag-readiness-history-index.json' -Entries $entries

        { & $promotionGateScript -ReadinessRoot $gateRoot -FailOnBlock } | Should Throw

        { & $promotionGateScript -ReadinessRoot $gateRoot -AllowDirectorOverride -DirectorApprovalReference 'REL-OPS-DIR-123' -FailOnBlock } | Should Not Throw

        $reportPath = Join-Path $gateRoot 'release-ops-promotion-gate-report.json'
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $report.gatePassed | Should Be $true
        $report.promotionVerdict | Should Be 'director-approval-required'
        $report.directorOverride.used | Should Be $true
    }

    It 'update-release-ops-promotion-gate-trend archives latest report and summarizes pass/director/fail counts' {
        $trendRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\promotion-trend-a'
        if (-not (Test-Path -LiteralPath $trendRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $trendRoot -Force | Out-Null
            $script:createdFiles += $trendRoot
        }

        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/promotion-trend-a/release-ops-promotion-gate-report.json' -Verdict 'director-approval-required' -GatePassed $true -OverrideUsed $true -GeneratedAtUtc ([DateTimeOffset]::UtcNow)
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/promotion-trend-a/release-ops-promotion-gate-report.md' -Content '# latest' | Out-Null

        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/promotion-trend-a/release-ops-promotion-gate-report-20260101010101.json' -Verdict 'pass' -GatePassed $true -OverrideUsed $false -GeneratedAtUtc ([DateTimeOffset]::UtcNow.AddDays(-2))
        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/promotion-trend-a/release-ops-promotion-gate-report-20251201010101.json' -Verdict 'fail' -GatePassed $false -OverrideUsed $false -GeneratedAtUtc ([DateTimeOffset]::UtcNow.AddDays(-3))

        { & $promotionGateTrendScript -ReadinessRoot $trendRoot -ArchiveLatest -MaxEntries 20 -RetentionDays 365 } | Should Not Throw

        $indexPath = Join-Path $trendRoot 'release-ops-promotion-gate-trend-index.json'
        (Test-Path -LiteralPath $indexPath -PathType Leaf) | Should Be $true

        $index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
        $index.verdictCounts.pass | Should Be 1
        $index.verdictCounts.directorApprovalRequired | Should Be 1
        $index.verdictCounts.fail | Should Be 1
        $index.directorOverrideUsedCount | Should Be 1
        $index.blockedCount | Should Be 1
    }

    It 'update-release-ops-promotion-gate-trend prunes stale archived reports' {
        $trendRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\promotion-trend-b'
        if (-not (Test-Path -LiteralPath $trendRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $trendRoot -Force | Out-Null
            $script:createdFiles += $trendRoot
        }

        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/promotion-trend-b/release-ops-promotion-gate-report-20000101010101.json' -Verdict 'fail' -GatePassed $false -OverrideUsed $false -GeneratedAtUtc ([DateTimeOffset]::UtcNow.AddYears(-10))
        $stalePath = Join-Path $trendRoot 'release-ops-promotion-gate-report-20000101010101.json'
        (Get-Item -LiteralPath $stalePath).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-500)

        { & $promotionGateTrendScript -ReadinessRoot $trendRoot -MaxEntries 20 -RetentionDays 365 } | Should Not Throw

        (Test-Path -LiteralPath $stalePath -PathType Leaf) | Should Be $false
    }

    It 'generate-release-ops-closure-package-manifest links required artifacts for release tag' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        $closureRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-a'
        if (-not (Test-Path -LiteralPath $closureRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $closureRoot -Force | Out-Null
            $script:createdFiles += $closureRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-a/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-a/release-ops-tag-readiness-history-index.json' -Content '{}' | Out-Null
        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-a/release-ops-promotion-gate-report.json' -Verdict 'pass' -GatePassed $true -OverrideUsed $false
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-a/release-ops-promotion-gate-trend-index.json' -Content '{}' | Out-Null

        { & $closureManifestScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Not Throw

        $manifestPath = Join-Path $closureRoot 'release-ops-closure-package-manifest.json'
        (Test-Path -LiteralPath $manifestPath -PathType Leaf) | Should Be $true

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifest.missingRequiredCount | Should Be 0
        ($manifest.linkedArtifacts | Where-Object { -not $_.exists }).Count | Should Be 0
    }

    It 'generate-release-ops-closure-package-manifest fails when required artifact is missing' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        $closureRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-b'
        if (-not (Test-Path -LiteralPath $closureRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $closureRoot -Force | Out-Null
            $script:createdFiles += $closureRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-b/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-b/release-ops-tag-readiness-history-index.json' -Content '{}' | Out-Null
        # Intentionally omit promotion gate report JSON to trigger missing required artifact failure.
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-b/release-ops-promotion-gate-trend-index.json' -Content '{}' | Out-Null

        { & $closureManifestScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Throw
    }

    It 'check-release-ops-closure-package-manifest passes for complete generated closure manifest' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        $closureRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-c'
        if (-not (Test-Path -LiteralPath $closureRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $closureRoot -Force | Out-Null
            $script:createdFiles += $closureRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-c/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-c/release-ops-tag-readiness-history-index.json' -Content '{}' | Out-Null
        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-c/release-ops-promotion-gate-report.json' -Verdict 'pass' -GatePassed $true -OverrideUsed $false
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-c/release-ops-promotion-gate-trend-index.json' -Content '{}' | Out-Null

        { & $closureManifestScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Not Throw
        { & $closureManifestCheckScript -ReadinessRoot $closureRoot -TagName "v$script:testVersion" } | Should Not Throw
    }

    It 'check-release-ops-closure-package-manifest fails when required linked artifact is missing on disk' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        $closureRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-d'
        if (-not (Test-Path -LiteralPath $closureRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $closureRoot -Force | Out-Null
            $script:createdFiles += $closureRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-d/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-d/release-ops-tag-readiness-history-index.json' -Content '{}' | Out-Null
        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-d/release-ops-promotion-gate-report.json' -Verdict 'pass' -GatePassed $true -OverrideUsed $false
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-d/release-ops-promotion-gate-trend-index.json' -Content '{}' | Out-Null

        { & $closureManifestScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Not Throw

        Remove-Item -LiteralPath (Join-Path $closureRoot 'release-ops-promotion-gate-report.json') -Force

        { & $closureManifestCheckScript -ReadinessRoot $closureRoot -TagName "v$script:testVersion" } | Should Throw
    }

    It 'check-release-ops-closure-package-drift passes when linked readiness and promotion outputs are unchanged' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        $closureRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-e'
        if (-not (Test-Path -LiteralPath $closureRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $closureRoot -Force | Out-Null
            $script:createdFiles += $closureRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-e/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-e/release-ops-tag-readiness-history-index.json' -Content '{}' | Out-Null
        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-e/release-ops-promotion-gate-report.json' -Verdict 'pass' -GatePassed $true -OverrideUsed $false
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-e/release-ops-promotion-gate-trend-index.json' -Content '{}' | Out-Null

        { & $closureManifestScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Not Throw
        { & $closureManifestDriftScript -ReadinessRoot $closureRoot -TagName "v$script:testVersion" } | Should Not Throw
    }

    It 'check-release-ops-closure-package-drift fails when linked promotion output is modified after manifest generation' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        $closureRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-f'
        if (-not (Test-Path -LiteralPath $closureRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $closureRoot -Force | Out-Null
            $script:createdFiles += $closureRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-f/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-f/release-ops-tag-readiness-history-index.json' -Content '{}' | Out-Null
        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-f/release-ops-promotion-gate-report.json' -Verdict 'pass' -GatePassed $true -OverrideUsed $false
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-f/release-ops-promotion-gate-trend-index.json' -Content '{}' | Out-Null

        { & $closureManifestScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Not Throw

        $promotionReportPath = Join-Path $closureRoot 'release-ops-promotion-gate-report.json'
        Add-Content -LiteralPath $promotionReportPath -Value "`n# drift"
        (Get-Item -LiteralPath $promotionReportPath).LastWriteTimeUtc = [DateTime]::UtcNow.AddMinutes(1)

        { & $closureManifestDriftScript -ReadinessRoot $closureRoot -TagName "v$script:testVersion" } | Should Throw
    }

    It 'generate-release-ops-closure-package-integrity-report emits pass verdict with hashes for complete closure package' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        $closureRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-g'
        if (-not (Test-Path -LiteralPath $closureRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $closureRoot -Force | Out-Null
            $script:createdFiles += $closureRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-g/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-g/release-ops-tag-readiness-history-index.json' -Content '{}' | Out-Null
        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-g/release-ops-promotion-gate-report.json' -Verdict 'pass' -GatePassed $true -OverrideUsed $false
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-g/release-ops-promotion-gate-trend-index.json' -Content '{}' | Out-Null

        { & $closureManifestScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Not Throw
        { & $closureIntegrityReportScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Not Throw

        $reportPath = Join-Path $closureRoot 'release-ops-closure-package-integrity-report.json'
        (Test-Path -LiteralPath $reportPath -PathType Leaf) | Should Be $true

        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $report.integrityVerdict | Should Be 'pass'
        $report.issueCount | Should Be 0
        $report.verifiedArtifactCount | Should BeGreaterThan 0
        ([string]$report.manifest.sha256).Length | Should Be 64
    }

    It 'generate-release-ops-closure-package-integrity-report fails with FailOnIssues when linked artifact is missing' {
        New-EvidenceFixture -Version $script:testVersion -DateStamp $script:testDateStamp -IncludeIndex -IncludeIndexEntries | Out-Null

        $closureRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-h'
        if (-not (Test-Path -LiteralPath $closureRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $closureRoot -Force | Out-Null
            $script:createdFiles += $closureRoot
        }

        New-TagReadinessSummaryFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-h/release-ops-tag-readiness-summary.json' -TagName "v$script:testVersion" -Verdict 'ready' -DiagnosticsOverallStatus 'pass' -ValidatorFailed 0
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-h/release-ops-tag-readiness-history-index.json' -Content '{}' | Out-Null
        New-PromotionGateReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-h/release-ops-promotion-gate-report.json' -Verdict 'pass' -GatePassed $true -OverrideUsed $false
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-h/release-ops-promotion-gate-trend-index.json' -Content '{}' | Out-Null

        { & $closureManifestScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" } | Should Not Throw

        Remove-Item -LiteralPath (Join-Path $closureRoot 'release-ops-promotion-gate-report.json') -Force

        { & $closureIntegrityReportScript -ReadinessRoot $closureRoot -OutputDir $closureRoot -TagName "v$script:testVersion" -FailOnIssues } | Should Throw
    }

    It 'update-release-ops-closure-package-integrity-history archives latest report and writes trend index' {
        $integrityRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-i'
        if (-not (Test-Path -LiteralPath $integrityRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $integrityRoot -Force | Out-Null
            $script:createdFiles += $integrityRoot
        }

        New-ClosureIntegrityReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-i/release-ops-closure-package-integrity-report.json' -TagName "v$script:testVersion" -Verdict 'pass' -IssueCount 0 -ManifestSha 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' -GeneratedAtUtc ([DateTimeOffset]::UtcNow.AddMinutes(-1))
        New-TestFile -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-i/release-ops-closure-package-integrity-report.md' -Content '# integrity latest' | Out-Null

        { & $closureIntegrityHistoryScript -ReadinessRoot $integrityRoot -ArchiveLatest -MaxEntries 20 -RetentionDays 365 } | Should Not Throw

        $indexPath = Join-Path $integrityRoot 'release-ops-closure-package-integrity-history-index.json'
        (Test-Path -LiteralPath $indexPath -PathType Leaf) | Should Be $true

        $archived = @(Get-ChildItem -LiteralPath $integrityRoot -File -Filter 'release-ops-closure-package-integrity-report-*.json' |
            Where-Object { $_.Name -ne 'release-ops-closure-package-integrity-report.json' })
        $archived.Count | Should BeGreaterThan 0

        $index = Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
        $index.totalEntries | Should BeGreaterThan 0
        $index.verdictCounts.pass | Should Be 1
        $index.uniqueManifestHashCount | Should Be 1
    }

    It 'update-release-ops-closure-package-integrity-history prunes stale archived reports' {
        $integrityRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-j'
        if (-not (Test-Path -LiteralPath $integrityRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $integrityRoot -Force | Out-Null
            $script:createdFiles += $integrityRoot
        }

        New-ClosureIntegrityReportFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-j/release-ops-closure-package-integrity-report-20000101010101-v0.0.1.json' -TagName 'v0.0.1' -Verdict 'fail' -IssueCount 2 -ManifestSha 'cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc' -GeneratedAtUtc ([DateTimeOffset]::UtcNow.AddYears(-10))
        $stalePath = Join-Path $integrityRoot 'release-ops-closure-package-integrity-report-20000101010101-v0.0.1.json'
        (Get-Item -LiteralPath $stalePath).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-500)

        { & $closureIntegrityHistoryScript -ReadinessRoot $integrityRoot -MaxEntries 20 -RetentionDays 365 } | Should Not Throw

        (Test-Path -LiteralPath $stalePath -PathType Leaf) | Should Be $false
    }

    It 'check-release-ops-closure-package-integrity-gate passes when history has sufficient recent pass entries' {
        $gateRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-k'
        if (-not (Test-Path -LiteralPath $gateRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $gateRoot -Force | Out-Null
            $script:createdFiles += $gateRoot
        }

        $entries = @(
            [pscustomobject]@{ integrityVerdict = 'pass'; tagName = "v$script:testVersion"; generatedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-2).ToString('o'); issueCount = 0; manifestSha256 = 'dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd'; fileName = "release-ops-closure-package-integrity-report-pass1.json" },
            [pscustomobject]@{ integrityVerdict = 'pass'; tagName = "v$script:testVersion"; generatedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-5).ToString('o'); issueCount = 0; manifestSha256 = 'dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd'; fileName = "release-ops-closure-package-integrity-report-pass2.json" }
        )
        New-ClosureIntegrityHistoryIndexFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-k/release-ops-closure-package-integrity-history-index.json' -Entries $entries

        { & $closureIntegrityGateScript -ReadinessRoot $gateRoot -MinRecentPassCount 1 -RecentWindowCount 5 -FailOnBlock } | Should Not Throw

        $reportPath = Join-Path $gateRoot 'release-ops-closure-package-integrity-gate-report.json'
        (Test-Path -LiteralPath $reportPath -PathType Leaf) | Should Be $true
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $report.gateVerdict | Should Be 'pass'
        $report.recentPassCount | Should Be 2
    }

    It 'check-release-ops-closure-package-integrity-gate blocks and throws with FailOnBlock when no recent pass entries exist' {
        $gateRoot = Join-Path $repoRoot 'artifacts\release-ops-tag-readiness-tests\closure-l'
        if (-not (Test-Path -LiteralPath $gateRoot -PathType Container)) {
            New-Item -ItemType Directory -Path $gateRoot -Force | Out-Null
            $script:createdFiles += $gateRoot
        }

        $entries = @(
            [pscustomobject]@{ integrityVerdict = 'fail'; tagName = "v$script:testVersion"; generatedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-2).ToString('o'); issueCount = 1; manifestSha256 = 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'; fileName = "release-ops-closure-package-integrity-report-fail1.json" }
        )
        New-ClosureIntegrityHistoryIndexFixture -RelativePath 'artifacts/release-ops-tag-readiness-tests/closure-l/release-ops-closure-package-integrity-history-index.json' -Entries $entries

        { & $closureIntegrityGateScript -ReadinessRoot $gateRoot -MinRecentPassCount 1 -RecentWindowCount 5 -FailOnBlock } | Should Throw

        $reportPath = Join-Path $gateRoot 'release-ops-closure-package-integrity-gate-report.json'
        (Test-Path -LiteralPath $reportPath -PathType Leaf) | Should Be $true
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $report.gateVerdict | Should Be 'block'
        $report.recentPassCount | Should Be 0
    }
}
